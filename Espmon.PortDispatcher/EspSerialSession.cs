#pragma warning disable CS0649
using Microsoft.Win32.SafeHandles;
#if !ESS_NO_PORT_ENUM
using Microsoft.Management.Infrastructure;
#endif
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Espmon;
#if !ESS_NO_PORT_ENUM
public sealed class PortEntry
{
    public PortEntry(string portName, string serialNumber)
    {
        PortName = portName;
        SerialNumber = serialNumber;
    }
    public string PortName { get; }
    public string SerialNumber { get; }

}
#endif
public sealed class FrameReceivedEventArgs : EventArgs
{
    public byte Command;
    public byte[] Data { get; }

    public FrameReceivedEventArgs(byte command, byte[] data)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(command, 127, nameof(command));
        Command = command;
        Data = data;
    }
}
public sealed class FrameErrorEventArgs : EventArgs
{
    public byte Command { get; }
    public byte Seq { get; }
    public int Attempts { get; }   // initial send + resends made before giving up
    public FrameErrorEventArgs(byte command, byte seq, int attempts)
    { Command = command; Seq = seq; Attempts = attempts; }
}

public sealed class ResendRequestedEventArgs : EventArgs
{
    public byte Command { get; }
    public byte Seq { get; }
    public ResendRequestedEventArgs(byte command, byte seq)
    { Command = command; Seq = seq; }
}
[SupportedOSPlatform("windows")]
internal partial class EspSerialSession : IDisposable
{
    struct StateMachine
    {
        int state;
        byte rawCmd;
        byte rawSeq;     // seq/type byte
        uint rawLen;
        uint rawCrc;

        public byte RawCommandByte => rawCmd;
        public byte RawSeqByte => rawSeq;
        public byte Command => (byte)(rawCmd - 128);   // 0 for control frames
        public int FrameType => (rawSeq >> 6) & 0x03;   // 0=DATA 1=ACK 2=NACK
        public byte Seq => (byte)(rawSeq & 0x3F);
        public bool IsControl => rawCmd == 128 || FrameType != 0;
        public int Length => (int)rawLen;
        public uint Crc => rawCrc;
        public bool IsDone => state == 17;

        public void Reset() => state = 0;

        public bool Step(List<byte>? log, object? logLock, byte data)
        {
            if (state == 17) state = 0;         // previous frame consumed; start fresh

            if (state == 0)
            {
                if (data < 128)                 // sub-128 byte = transport-level log text
                {
                    if (logLock != null) lock (logLock) { log?.Add(data); }
                    return false;
                }
                state = 1;
                rawCmd = data;
                rawSeq = 0; rawLen = 0; rawCrc = 0;
            }
            else if (state < 8)                 // remaining 7 marker bytes must match
            {
                if (rawCmd != data)
                {
                    if (logLock != null) lock (logLock)
                    {
                        for (var i = 0; i < state; ++i) log?.Add(rawCmd);
                        log?.Add(data);
                    }
                    state = 0;
                    return false;
                }
                ++state;
            }
            else if (state == 8) { rawSeq = data; ++state; }                       // seq/type
            else if (state < 13) { rawLen |= (uint)data << (8 * (state - 9)); ++state; } // len LE
            else /* state<17 */   { rawCrc |= (uint)data << (8 * (state - 13)); ++state; } // crc LE
            return true;
        }
    }

    /// <summary>
    /// Captures the state for one pending overlapped I/O so the
    /// IOCallback can resolve the right TaskCompletionSource.
    /// </summary>
    private sealed class ReadOp
    {
        public TaskCompletionSource<int> Tcs = null!;
        public unsafe NativeOverlapped* Overlapped;
    }

    private sealed class CommEventOp
    {
        public TaskCompletionSource<int> Tcs = null!;
        public unsafe NativeOverlapped* Overlapped;
        public GCHandle MaskHandle;
    }

    volatile bool _closing;
    volatile bool _connErrorFired;
    string _portName;
    bool _disposed;
    SafeFileHandle? _handle;
    ThreadPoolBoundHandle? _boundHandle;
    readonly List<byte> _log;
    readonly object _logLock;
    readonly object _ioLock;
    bool _logging;
    SynchronizationContext? _sync;
    Task? _readTask, _statTask;
    IntPtr _powerNotifyHandle;
    readonly byte[] _rx = new byte[4096];
    int _rxHead;   // next unread byte in _rx
    int _rxTail;   // number of valid bytes in _rx
    bool _deliveryNotified;   // add alongside the other ARQ fields

    const int FrameHeaderLength = 17;          // 8 marker + 1 seq/type + 4 len + 4 crc
    const int MaxFrameLength = 32768;        // read bound; over this we NACK+resync
    const int TypeData = 0, TypeAck = 1, TypeNack = 2;

    readonly object _arq = new object();   // guards the ARQ state below
    readonly object _sendLock = new object();   // keeps one frame contiguous on the wire

    byte _txSeq = 0x3F;                  // last DATA seq sent; first send -> 0
    byte _expectedRxSeq = 0;                     // next DATA seq we expect to receive
    bool _awaiting;                             // a sent DATA frame is unacked
    byte[]? _retain;                            // last DATA frame bytes, for resend
                                                // chunk 2 also adds: _ackTimeoutMs, _maxRetries, _retries, _sendQueue, _ackTimer
    DeviceNotifyCallbackRoutine? _powerCallback; // kept rooted: native code holds a pointer to it
    static readonly uint[] _crcTable = BuildCrcTable();
    int _ackTimeoutMs;                 // -1 = explicit mode: NACK -> event, no timer, no auto-give-up
    int _maxRetries = 5;
    int _retries;                      // timeout-driven resends so far (NACKs never counted)
    readonly Queue<(byte cmd, byte[] data)> _sendQueue = new();
    System.Threading.Timer? _ackTimer;

    public event EventHandler<ResendRequestedEventArgs>? ResendRequested;  // explicit mode only
    public event EventHandler<EventArgs>? ConnectionError;
    public event EventHandler<FrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<FrameErrorEventArgs>? FrameError;
#if !ESS_NO_PORT_ENUM
    public static PortEntry[] GetPorts()
    {
        var result = new List<PortEntry>();
        using var session = CimSession.Create(null); // null = local machine
        var instances = session.QueryInstances(@"root\cimv2", "WQL",
            "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE ClassGuid = '{4d36e978-e325-11ce-bfc1-08002be10318}'");

        foreach (var instance in instances)
        {
            var deviceId = instance.CimInstanceProperties["DeviceID"]?.Value as string;
            if (string.IsNullOrEmpty(deviceId))
                continue;

            // Extract Serial
            int index = deviceId.LastIndexOf('\\');
            if (index == -1)
                continue;

            string serialNo = deviceId.Substring(index + 1);

            // Extract port name from Name property
            var nameValue = instance.CimInstanceProperties["Name"]?.Value as string;
            if (string.IsNullOrEmpty(nameValue))
                continue;

            int idx = nameValue.IndexOf('(');
            if (idx > -1)
            {
                int lidx = nameValue.IndexOf(')', idx + 2);
                if (lidx > -1)
                {
                    string extractedName = nameValue.Substring(idx + 1, lidx - idx - 1);
                    result.Add(new PortEntry(extractedName, serialNo));
                }
            }

        }
        return result.ToArray();

    }
#endif
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Close();
                _ackTimer?.Dispose();
                _ackTimer = null;
                lock (_arq) { _awaiting = false; _retries = 0; _sendQueue.Clear(); }
            }
            // Also runs on the finalizer path (disposing == false), where
            // Close() is not called. Only touches the IntPtr handle, so it is
            // finalizer-safe, and prevents the callback delegate from being
            // collected while the native registration is still live.
            UnregisterPowerNotification();
            _disposed = true;
        }
    }

    ~EspSerialSession()
    {
        Dispose(false);
    }

    void IDisposable.Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    byte[] BuildDataFrame(byte cmd, byte seq, ReadOnlySpan<byte> payload)
    {
        byte marker = (byte)(cmd + 128);
        byte seqByte = (byte)(seq & 0x3F);        // TypeData (0) in the high bits
        var frame = new byte[FrameHeaderLength + payload.Length];
        for (int i = 0; i < 8; ++i) frame[i] = marker;
        frame[8] = seqByte;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(9, 4), payload.Length);
        payload.CopyTo(frame.AsSpan(FrameHeaderLength));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(13, 4),
            Crc32(seqByte, payload.Length, payload));
        return frame;
    }

    byte[] BuildControlFrame(int type, byte seq)
    {
        byte seqByte = (byte)((type << 6) | (seq & 0x3F));
        var frame = new byte[FrameHeaderLength];
        for (int i = 0; i < 8; ++i) frame[i] = 128;   // control marker = cmd 0
        frame[8] = seqByte;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(9, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(13, 4),
            Crc32(seqByte, 0, ReadOnlySpan<byte>.Empty));
        return frame;
    }

    void SendControl(int type, byte seq) => WriteFrame(BuildControlFrame(type, seq));
    // Stamp next seq, build+retain, put on the wire, mark outstanding.
    void TransmitData(byte cmd, ReadOnlySpan<byte> payload)
    {
        byte[] frame;
        lock (_arq)
        {
            _txSeq = (byte)((_txSeq + 1) & 0x3F);
            frame = BuildDataFrame(cmd, _txSeq, payload);
            _retain = frame;
            _awaiting = true;
        }
        WriteFrame(frame);
        // chunk 2: arm the ack timer here when _ackTimeoutMs > 0
    }

    public bool Resend()
    {
        byte[]? frame;
        lock (_arq) { if (!_awaiting || _retain == null) return false; frame = _retain; }
        WriteFrame(frame);
        return true;
    }
    void WriteFrame(ReadOnlySpan<byte> frame)
    {
        lock (_sendLock)                              // one whole frame at a time on the wire
        {
            try { WriteAll(frame); }
            catch (Win32Exception) { OnConnectionError(EventArgs.Empty); Dispose(true); }
        }
    }
    // Called holding _arq. Stamps seq, builds+retains, arms timer, returns bytes to write.
    byte[] PrepareTransmit(byte cmd, byte[] payload)
    {
        _txSeq = (byte)((_txSeq + 1) & 0x3F);
        var frame = BuildDataFrame(cmd, _txSeq, payload);
        _retain = frame;
        _awaiting = true;
        _retries = 0;
        _deliveryNotified = false;
        ArmAckTimer();
        return frame;
    }

    public void Send(byte cmd, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cmd, 127, nameof(cmd));
        if (cmd < 1) throw new ArgumentOutOfRangeException(nameof(cmd), "cmd must be 1..127");
        if (data.Length > MaxFrameLength) throw new ArgumentOutOfRangeException(nameof(data));

        byte[] copy = data.ToArray();      // stop-and-wait: caller's span need not outlive the call
        byte[]? toWrite = null;
        lock (_arq)
        {
            if (_awaiting) _sendQueue.Enqueue((cmd, copy));   // one in flight; queue the rest
            else toWrite = PrepareTransmit(cmd, copy);
        }
        if (toWrite != null) WriteFrame(toWrite);
    }
    private unsafe void WriteAll(ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                NativeOverlapped* ov;

                lock (_ioLock)
                {
                    if (_closing || _boundHandle == null || _boundHandle.Handle.IsClosed || _boundHandle.Handle.IsInvalid || _handle == null)
                        return; // port is closing/closed — silently drop the write

                    ov = _boundHandle.AllocateNativeOverlapped(
                        (errorCode, numBytes, pOv) =>
                        {
                            try { _boundHandle?.FreeNativeOverlapped(pOv); }
                            catch (ObjectDisposedException) { } // lost race with dispose; overlapped is cleaned up by handle disposal
                            if (errorCode == 0) tcs.TrySetResult((int)numBytes);
                            else tcs.TrySetException(new Win32Exception((int)errorCode));
                        },
                        null, null);

                    int written0 = 0;
                    if (!WriteFile(_handle, ptr + offset, data.Length - offset, ref written0, ov))
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err != ERROR_IO_PENDING)
                        {
                            _boundHandle.FreeNativeOverlapped(ov);
                            throw new Win32Exception(err);
                        }
                    }
                }

                int written = tcs.Task.GetAwaiter().GetResult(); // block outside the lock
                offset += written;
            }
        }
    }

    private void OnConnectionError(EventArgs args)
    {
        if (_disposed) return;
        if (_connErrorFired) return;
        _connErrorFired = true;
        if (ConnectionError != null)
        {
            if (_sync == null)
            {
                ConnectionError?.Invoke(this, args);
            }
            else
            {
                _sync.Post((state) => ConnectionError?.Invoke(this, args), null);
            }
        }
    }

    public byte[] GetNextLogData()
    {
        lock (_logLock)
        {
            var res = _log.ToArray();
            _log.Clear();
            return res;
        }
    }

    private void OnFrameReceived(FrameReceivedEventArgs args)
    {
        if (_disposed) return;
        if (FrameReceived != null)
        {
            if (_sync == null)
            {
                FrameReceived?.Invoke(this, args);
            }
            else
            {
                _sync.Post((state) => FrameReceived?.Invoke(this, args), null);
            }
        }
    }

    private void OnFrameError(FrameErrorEventArgs args)
    {
        if (_disposed) return;
        if (FrameError != null)
        {
            if (_sync == null)
            {
                FrameError?.Invoke(this, args);
            }
            else
            {
                _sync.Post((state) => FrameError?.Invoke(this, args), null);
            }
        }
    }

    /// <summary>
    /// Overlapped read via IOCP.  The CLR's thread pool dispatches the
    /// completion callback — no manual event handles or registered waits.
    /// </summary>
    private unsafe Task<int> ReadAsync(byte[] buffer, int offset, int count)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_boundHandle != null && _handle != null)
        {
            var ov = _boundHandle.AllocateNativeOverlapped(
                (errorCode, numBytes, pOv) =>
                {
                    _boundHandle.FreeNativeOverlapped(pOv);
                    if (errorCode == 0)
                        tcs.TrySetResult((int)numBytes);
                    else if (errorCode == ERROR_OPERATION_ABORTED)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetException(new Win32Exception((int)errorCode));
                },
                null,
                buffer);  // pins buffer until FreeNativeOverlapped

            int read = 0;
            fixed (byte* pBuf = &buffer[offset])
            {
                if (!ReadFile(_handle, pBuf, count, ref read, ov))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_IO_PENDING)
                    {
                        _boundHandle.FreeNativeOverlapped(ov);
                        if (err == ERROR_OPERATION_ABORTED)
                            tcs.TrySetCanceled();
                        else
                            tcs.TrySetException(new Win32Exception(err));
                    }
                    // else: pending — IOCP callback will fire
                }
                // else: completed synchronously — IOCP callback still fires for bound handles
            }
        }
        else throw new InvalidOperationException("The port is not open");
        return tcs.Task;
    }

    private async Task ReadExactlyAsync(byte[] buffer, int count)
    {
        int got = 0;
        while (got < count)
        {
            if (_rxHead >= _rxTail)                       // buffer drained
            {
                _rxTail = await ReadAsync(_rx, 0, _rx.Length); // one real overlapped read
                _rxHead = 0;
                if (_rxTail == 0) continue;               // spurious return; pend again
            }
            int n = Math.Min(_rxTail - _rxHead, count - got);
            Buffer.BlockCopy(_rx, _rxHead, buffer, got, n);
            _rxHead += n;
            got += n;
        }
    }
    /// <summary>
    /// Overlapped WaitCommEvent via IOCP.
    /// WaitCommEvent writes the event mask to an int — we pin it via GCHandle
    /// and read it in the callback.
    /// </summary>
    private unsafe Task<int> WaitCommEventAsync()
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var maskArr = new int[1];
        var maskPin = GCHandle.Alloc(maskArr, GCHandleType.Pinned);
        if (_boundHandle != null && _handle != null)
        {
            var ov = _boundHandle.AllocateNativeOverlapped(
                (errorCode, numBytes, pOv) =>
                {
                    int mask = maskArr[0];
                    maskPin.Free();
                    _boundHandle.FreeNativeOverlapped(pOv);
                    if (errorCode == 0)
                        tcs.TrySetResult(mask);
                    else if (errorCode == ERROR_OPERATION_ABORTED)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetException(new Win32Exception((int)errorCode));
                },
                null, null);
            if (!WaitCommEvent(_handle, ref maskArr[0], ov))
            {
                int err = Marshal.GetLastWin32Error();
                if (err != ERROR_IO_PENDING)
                {
                    maskPin.Free();
                    _boundHandle.FreeNativeOverlapped(ov);
                    if (err == ERROR_OPERATION_ABORTED)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetException(new Win32Exception(err));
                }
            }

        }
        else throw new InvalidOperationException("The port is not open");


        return tcs.Task;
    }
    public bool IsOpen
    {
        get
        {
            return _handle != null && !_handle.IsInvalid && !_handle.IsClosed;
        }
    }
    private void OnResendRequested(ResendRequestedEventArgs args)
    {
        if (_disposed) return;
        if (ResendRequested != null)
        {
            if (_sync == null)
                ResendRequested?.Invoke(this, args);
            else
                _sync.Post((state) => ResendRequested?.Invoke(this, args), null);
        }
    }
    void HandleAck(byte seq)
    {
        byte[]? next = null;
        lock (_arq)
        {
            if (!_awaiting || seq != _txSeq) return;   // stale/duplicate ack
            _awaiting = false;
            DisarmAckTimer();
            if (_sendQueue.Count > 0)
            {
                var (cmd, data) = _sendQueue.Dequeue();
                next = PrepareTransmit(cmd, data);
            }
        }
        if (next != null) WriteFrame(next);
    }

    void HandleNack(byte seq)
    {
        bool explicitMode; byte cmd, s;
        lock (_arq)
        {
            if (!_awaiting) return;
            explicitMode = _ackTimeoutMs < 0;
            cmd = (byte)(_retain![0] - 128);
            s = _txSeq;
            // deliberately: no _retries change, no ArmAckTimer() — the running timeout guards termination
        }
        if (explicitMode) OnResendRequested(new ResendRequestedEventArgs(cmd, s));
        else Resend();
    }

    void OnAckTimeout(object? _)
    {
        byte[]? resend = null;
        FrameErrorEventArgs? failure = null;
        lock (_arq)
        {
            if (!_awaiting || _ackTimeoutMs <= 0) return;
            resend = _retain;                       // always keep retransmitting the same frame
            _retries++;
            if (_retries >= _maxRetries && !_deliveryNotified)
            {
                _deliveryNotified = true;           // one-shot "not getting through" notice
                failure = new FrameErrorEventArgs((byte)(_retain![0] - 128), _txSeq, _retries);
            }
            ArmAckTimer();                          // never stops on its own; only an ACK or Close() ends it
        }
        if (resend != null) WriteFrame(resend);
        if (failure != null) OnFrameError(failure);
    }
    byte VolatileExpectedRxSeq() { lock (_arq) return _expectedRxSeq; }

    void DispatchValidFrame(byte rawCmd, byte rawSeq, byte[] payload)
    {
        int type = (rawSeq >> 6) & 0x03;
        byte seq = (byte)(rawSeq & 0x3F);

        if (rawCmd == 128 || type != TypeData)        // control frame
        {
            if (type == TypeAck) HandleAck(seq);
            else if (type == TypeNack) HandleNack(seq);
            return;                                    // reserved types ignored
        }

        byte cmd = (byte)(rawCmd - 128);               // DATA frame
        byte expected; lock (_arq) expected = _expectedRxSeq;

        if (seq == expected)
        {
            SendControl(TypeAck, seq);                 // ack = "received intact"
            lock (_arq) _expectedRxSeq = (byte)((seq + 1) & 0x3F);
            OnFrameReceived(new FrameReceivedEventArgs(cmd, payload));
        }
        else if (seq == (byte)((expected - 1) & 0x3F))
        {
            SendControl(TypeAck, seq);                 // duplicate (our ack was lost): re-ack only
        }
        else
        {
            SendControl(TypeNack, expected);           // gap: ask for what we expect
        }
    }
    public void Open()
    {
        if (IsOpen) return;
        _closing = false;
        _connErrorFired = false;
        lock (_arq)
        {
            _txSeq = 0x3F;          // first Send -> seq 0
            _expectedRxSeq = 0;
            _awaiting = false;
            _retries = 0;
            _sendQueue.Clear();
        }
        _ackTimer ??= new System.Threading.Timer(OnAckTimeout, null, Timeout.Infinite, Timeout.Infinite);
        var rawHandle = CreateFile(
                    $@"\\.\{_portName}",
                    GENERIC_READ | GENERIC_WRITE,
                    0,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);
        if (rawHandle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _handle = rawHandle;

        // Bind to the CLR's IOCP thread pool.  All overlapped completions
        // on this handle are now dispatched via the thread pool — no need
        // for manual event handles or RegisterWaitForSingleObject.
        _boundHandle = ThreadPoolBoundHandle.BindHandle(_handle);

        DCB dcb = default;
        dcb.DCBlength = (uint)Unsafe.SizeOf<DCB>();
        if (!GetCommState(_handle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        dcb.BaudRate = 115200;
        dcb.ByteSize = 8;
        dcb.Parity = 0;
        dcb.StopBits = 0;
        if (!SetCommState(_handle, ref dcb))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!SetCommMask(_handle, EV_RLSD))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        if (!SetupComm(_handle, 8192, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        var timeouts = new COMMTIMEOUTS
        {
            ReadIntervalTimeout = 10, // ms gap between bytes after which the read returns its burst
            ReadTotalTimeoutMultiplier = 0,
            ReadTotalTimeoutConstant = 0,  // 0 total => pend (0 CPU) until the FIRST byte, no overall timeout
            WriteTotalTimeoutMultiplier = 0,
            WriteTotalTimeoutConstant = 0,
        };
        if (!SetCommTimeouts(_handle, ref timeouts))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        RegisterPowerNotification();

        _statTask = Task.Factory.StartNew(async () =>
        {
            try
            {
                while (!_closing)
                {
                    int mask = await WaitCommEventAsync();
                    if ((mask & (int)EV_RLSD) != 0 && !_closing)
                    {
                        OnConnectionError(EventArgs.Empty);
                        break;
                    }
                }
            }
            catch (Exception)
            {
                if (!_closing)
                {
                    OnConnectionError(EventArgs.Empty);
                }
            }
        }, default, TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);

        _readTask = Task.Run(async () =>
        {
            byte[] tmp = new byte[1];
            StateMachine mach = default;
            try
            {
                while (!_closing)
                {
                    await ReadExactlyAsync(tmp, 1);
                    mach.Step(_log, _logging ? _logLock : null, tmp[0]);
                    if (mach.IsDone)
                    {
                        if (_closing) break;
                        int len = mach.Length;

                        if (len < 0 || len > MaxFrameLength)          // CRC-protected, but bound the read
                        {
                            SendControl(TypeNack, VolatileExpectedRxSeq());
                            mach.Reset();
                            continue;
                        }

                        byte[] payload = len > 0 ? new byte[len] : Array.Empty<byte>();
                        if (len > 0) await ReadExactlyAsync(payload, len);

                        if (Crc32(mach.RawSeqByte, len, payload) != mach.Crc)
                        {
                            SendControl(TypeNack, VolatileExpectedRxSeq());   // corrupt: seq untrustworthy
                            mach.Reset();
                            continue;
                        }

                        DispatchValidFrame(mach.RawCommandByte, mach.RawSeqByte, payload);
                        mach.Reset();
                    }
                }
            }
            catch (Exception)
            {
                if (!_closing)
                {
                    OnConnectionError(EventArgs.Empty);
                }
            }

        });
    }
    public void Close()
    {
        UnregisterPowerNotification();
        if (!IsOpen)
        {
            return;
        }
        _closing = true;
        Thread.MemoryBarrier();
        // CancelIoEx unblocks any pending ReadFile / WaitCommEvent.
        // They will complete with ERROR_OPERATION_ABORTED and the
        // IOCP callback will fire, resolving the tasks.
        try
        {
            if (_handle != null && !_handle.IsInvalid && !_handle.IsClosed)
            {
                CancelIoEx(_handle, IntPtr.Zero);
            }
        }
        catch (Win32Exception) { }
        try
        {
            if (_connErrorFired)
            {
                if (_statTask != null && _readTask != null)
                {
                    var tasks = new Task[2];
                    int taskCount = 0;
                    if (_readTask.Status == TaskStatus.Running)
                    {
                        tasks[taskCount++] = _readTask;
                    }
                    if (_statTask.Status == TaskStatus.Running)
                    {
                        tasks[taskCount++] = _statTask;
                    }
                    if (taskCount > 0)
                    {
                        Task.WaitAll(tasks[0..taskCount]);
                    }
                }
            }
        }
        catch (AggregateException)
        {

        }
        _boundHandle?.Dispose();
        _handle?.Dispose();
        _closing = false;
        _connErrorFired = false;
    }

    private void RegisterPowerNotification()
    {
        // Keep the delegate in a field so it stays rooted for the lifetime of
        // the registration; the OS holds a raw function pointer to it.
        _powerCallback = PowerCallback;
        var sub = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            Callback = Marshal.GetFunctionPointerForDelegate(_powerCallback),
            Context = IntPtr.Zero
        };
        // Callback-based notification: no window or message pump required, so
        // this works identically in a WinUI3 app and a headless service.
        uint rc = PowerRegisterSuspendResumeNotification(
            DEVICE_NOTIFY_CALLBACK, ref sub, out _powerNotifyHandle);
        if (rc != 0)
        {
            _powerCallback = null;
            _powerNotifyHandle = IntPtr.Zero;
            throw new Win32Exception((int)rc);
        }
    }

    private void UnregisterPowerNotification()
    {
        var h = Interlocked.Exchange(ref _powerNotifyHandle, IntPtr.Zero);
        if (h != IntPtr.Zero)
        {
            // Blocks until any in-progress callback returns, so after this the
            // delegate is safe to release. Must NOT be called from inside the
            // callback itself (would deadlock) — see OnSuspend.
            try { PowerUnregisterSuspendResumeNotification(h); }
            catch (Win32Exception) { }
        }
        _powerCallback = null;
    }

    private uint PowerCallback(IntPtr context, uint type, IntPtr setting)
    {
        // Runs on an OS power-management thread. Must return promptly and must
        // not block the suspend transition.
        if (type == PBT_APMSUSPEND)
        {
            OnSuspend();
        }
        return 0; // ERROR_SUCCESS
    }

    private void OnSuspend()
    {
        if (_closing || _disposed) return;

        // Abort any pending overlapped ReadFile / WaitCommEvent so the read and
        // status loops unwind immediately. This is the same teardown path as a
        // cable unplug: the loops complete with ERROR_OPERATION_ABORTED and
        // raise ConnectionError themselves.
        try
        {
            var h = _handle;
            if (h != null && !h.IsInvalid && !h.IsClosed)
            {
                CancelIoEx(h, IntPtr.Zero);
            }
        }
        catch (Win32Exception) { }

        // Notify the host off this callback thread. Raising it inline risks a
        // synchronous ConnectionError handler calling Close(), which would
        // re-enter PowerUnregisterSuspendResumeNotification on the callback
        // thread and deadlock. OnConnectionError is idempotent, so firing here
        // in addition to the loop paths above is harmless.
        ThreadPool.QueueUserWorkItem(_ => OnConnectionError(EventArgs.Empty));
    }
    public EspSerialSession(string port, bool logging = false,
                        SynchronizationContext? syncContext = null, int ackTimeoutMs = 1000)
    {
        _logLock = new object();
        _ioLock = new object();
        _sync = syncContext;
        _log = new List<byte>();
        _portName = port;
        _logging = logging;
        _ackTimeoutMs = ackTimeoutMs;
        _ackTimer = new System.Threading.Timer(OnAckTimeout, null, Timeout.Infinite, Timeout.Infinite);
    }

    public int AckTimeout { get { lock (_arq) return _ackTimeoutMs; } set { lock (_arq) _ackTimeoutMs = value; } }
    public int MaxRetries { get { lock (_arq) return _maxRetries; } set { lock (_arq) _maxRetries = value < 0 ? 0 : value; } }

    void ArmAckTimer() { if (_ackTimeoutMs > 0) _ackTimer?.Change(_ackTimeoutMs, Timeout.Infinite); }
    void DisarmAckTimer() { _ackTimer?.Change(Timeout.Infinite, Timeout.Infinite); }
    public bool IsLogging
    {
        get { return _logging; }
        set { _logging = value; }
    }
    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; ++n)
        {
            uint c = n;
            for (int k = 0; k < 8; ++k)
                c = (c & 1) != 0 ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
            t[n] = c;
        }
        return t;
    }
    static uint Crc32Byte(uint crc, byte b) => (crc >> 8) ^ _crcTable[(crc ^ b) & 0xFF];

    static uint Crc32(byte seqByte, int length, ReadOnlySpan<byte> payload)
    {
        uint c = 0xFFFFFFFFu;
        c = Crc32Byte(c, seqByte);
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, length);
        for (int i = 0; i < 4; ++i) c = Crc32Byte(c, len[i]);
        for (int i = 0; i < payload.Length; ++i) c = Crc32Byte(c, payload[i]);
        return c ^ 0xFFFFFFFFu;
    }

    #region Unmanaged
    private struct COMSTAT
    {
        public uint Flags;
        public uint cbInQue;
        public uint cbOutQue;
    }
    private struct DCB
    {
        public uint DCBlength;
        public uint BaudRate;
        public uint Flags;
        public ushort wReserved;
        public ushort XonLim;
        public ushort XoffLim;
        public byte ByteSize;
        public byte Parity;
        public byte StopBits;
        public byte XonChar;
        public byte XoffChar;
        public byte ErrorChar;
        public byte EofChar;
        public byte EvtChar;
        public ushort wReserved1;
    }
    const int ERROR_IO_PENDING = 0x000003E5;
    const uint ERROR_OPERATION_ABORTED = 995;
    const uint GENERIC_READ = 0x80000000;
    const uint GENERIC_WRITE = 0x40000000;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint EV_RLSD = 0x0020;
    const uint DEVICE_NOTIFY_CALLBACK = 0x00000002;
    const uint PBT_APMSUSPEND = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommMask(
        SafeFileHandle hFile,
        uint dwEvtMask);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool ClearCommError(
        SafeFileHandle hFile,
        ref int lpErrors,
        ref COMSTAT lpStat);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetCommState(
        SafeFileHandle hFile,
        ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetCommState(
        SafeFileHandle hFile,
        ref DCB lpDCB);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern bool SetupComm(
        SafeFileHandle hFile,     // handle to communications device 
        int dwInQueue,  // size of input buffer 
        int dwOutQueue  // size of output buffer
        );
    [StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS lpCommTimeouts);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool WaitCommEvent(
        SafeFileHandle hFile,
        ref int lpEvtMask,
        NativeOverlapped* lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern unsafe bool GetOverlappedResult(
        SafeFileHandle hFile,
        NativeOverlapped* lpOverlapped,
        ref int lpNumberOfBytesTransferred,
        bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadFile(
        SafeFileHandle hFile,
        byte* lpBuffer,
        int nNumberOfBytesToRead,
        ref int lpNumberOfBytesRead,
        NativeOverlapped* lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool WriteFile(
        SafeFileHandle hFile,
        byte* lpBuffer,
        int nNumberOfBytesToWrite,
        ref int lpNumberOfBytesWritten,
        NativeOverlapped* lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(
        SafeFileHandle hFile,
        IntPtr lpOverlapped);  // IntPtr.Zero = cancel all

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
    {
        public IntPtr Callback; // PDEVICE_NOTIFY_CALLBACK_ROUTINE (function pointer)
        public IntPtr Context;
    }

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerRegisterSuspendResumeNotification(
        uint flags,
        ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS recipient,
        out IntPtr registrationHandle);

    [DllImport("powrprof.dll", SetLastError = false)]
    private static extern uint PowerUnregisterSuspendResumeNotification(IntPtr registrationHandle);
    #endregion
}
#pragma warning restore