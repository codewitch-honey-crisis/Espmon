#pragma warning disable CS0649
using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Espmon;
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
    // Strong handle to 'this', round-tripped through the native Context token. Holding it
    // keeps the instance alive while registered; freed on unregister. Not pinned — the
    // token from ToIntPtr is opaque, not a pointer into the object.
    GCHandle _powerCallbackHandle;

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
    readonly object _closeLock;
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
   
    /// <summary>
    /// Enumerates present COM ports via SetupAPI and returns each port's name
    /// (e.g. "COM3") paired with the trailing segment of its device instance ID,
    /// which for USB devices is the descriptor serial number reported by the
    /// device (or a Windows-synthesized instance ID when the device has none).
    ///
    /// Replacement for the former Win32_PnPEntity WMI query. AOT/trim clean:
    /// plain P/Invoke over blittable signatures, no COM, no WMI. Output parity
    /// with the old implementation is preserved:
    ///   - PortName is read from the device's hardware key ("PortName" value),
    ///     which yields the same "COMx" string the old code parsed out of the
    ///     friendly name's parentheses, without depending on that string format.
    ///   - SerialNumber is the substring after the final '\' of the instance ID,
    ///     identical to the old DeviceID.Substring(LastIndexOf('\\') + 1).
    /// </summary>
    public static unsafe PortEntry[] GetPorts()
    {
        var result = new List<PortEntry>();

        IntPtr set;
        fixed (Guid* g = &GUID_DEVCLASS_PORTS)
            set = SetupDiGetClassDevsW(g, null, IntPtr.Zero, DIGCF_PRESENT);

        if (set == INVALID_HANDLE_VALUE)
            return Array.Empty<PortEntry>();

        try
        {
            var dev = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };

            // Allocate ONCE and reuse every iteration. stackalloc lifetime is the whole
            // method frame, not the loop body, so a per-iteration stackalloc accumulates
            // on the frame and can overflow the stack when many ports are present. Each
            // native call rewrites (and null-terminates) its buffer, so reuse is safe.
            const int idCap = 512;
            const int nameCap = 64;
            char* idBuf = stackalloc char[idCap];
            char* nameBuf = stackalloc char[nameCap];

            for (uint i = 0; ; i++)
            {
                if (!SetupDiEnumDeviceInfo(set, i, &dev))
                {
                    if (Marshal.GetLastWin32Error() == ERROR_NO_MORE_ITEMS)
                        break;
                    continue; // skip a transient failure on this index
                }

                // --- device instance id -> serial (trailing segment) ---
                uint required = 0;
                if (!SetupDiGetDeviceInstanceIdW(set, &dev, idBuf, idCap, &required))
                    continue;

                var instanceId = new string(idBuf); // read to the id's own NUL terminator
                int slash = instanceId.LastIndexOf('\\');
                string serial = slash >= 0 ? instanceId[(slash + 1)..] : instanceId;

                // --- PortName from the device's hardware key (e.g. "COM3") ---
                IntPtr hKey = SetupDiOpenDevRegKey(
                    set, &dev, DICS_FLAG_GLOBAL, 0, DIREG_DEV, KEY_READ);
                if (hKey == INVALID_HANDLE_VALUE)
                    continue;

                try
                {
                    uint cb = nameCap * sizeof(char); // in/out byte count
                    uint type = 0;

                    int rc;
                    fixed (char* name = "PortName")
                        rc = RegQueryValueExW(hKey, name, IntPtr.Zero, &type, (byte*)nameBuf, &cb);

                    if (rc != ERROR_SUCCESS) // includes ERROR_MORE_DATA if it didn't fit
                        continue;

                    int chars = (int)(cb / sizeof(char));
                    if (chars > nameCap) chars = nameCap; // defensive clamp
                    while (chars > 0 && nameBuf[chars - 1] == '\0') // REG_SZ may count a NUL
                        chars--;

                    // explicit length -> stale trailing chars from a prior iteration are ignored
                    var portName = new string(nameBuf, 0, chars);

                    // Class "Ports" also covers LPT; keep COM only (old code did too).
                    if (!portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                        continue;

                    result.Add(new PortEntry(portName, serial));
                }
                finally
                {
                    RegCloseKey(hKey);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result.ToArray();
    }
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
        lock (_sendLock)
        {
            try { WriteAll(frame); }
            catch (Win32Exception)
            {
                // Do NOT Dispose/Close here — this runs on the read-loop thread, and
                // Close() joins that same task (self-join). Just report; the owner's
                // ConnectionError handler drives teardown. Suppress during deliberate
                // close, like the loops do.
                if (!_closing) OnConnectionError(EventArgs.Empty);
            }
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
        var handler = ConnectionError;
        if (handler == null) return;

        // The loops call this inline. With _sync null, invoking directly runs the
        // host handler on the loop thread; if it calls Stop()/Close(), Close() waits
        // on the task it's running -> self-join. Post elsewhere so Close() is always
        // on another thread.
        if (_sync != null)
            _sync.Post(_ => handler(this, args), null);
        else
            ThreadPool.QueueUserWorkItem(_ => handler(this, args));
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
        var boundHandle = _boundHandle;   // capture once — free on THIS instance, never the field
        if (boundHandle != null && !boundHandle.Handle.IsClosed && !boundHandle.Handle.IsInvalid && _handle != null)
        {
            var ov = boundHandle.AllocateNativeOverlapped(
                (errorCode, numBytes, pOv) =>
                {
                    try { boundHandle.FreeNativeOverlapped(pOv); }
                    catch (ObjectDisposedException) { }   // lost race with a forced close; overlapped reclaimed via handle teardown
                    if (errorCode == 0)
                        tcs.TrySetResult((int)numBytes);
                    else if (errorCode == ERROR_OPERATION_ABORTED)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetException(new Win32Exception((int)errorCode));
                },
                null,
                buffer);

            int read = 0;
            fixed (byte* pBuf = &buffer[offset])
            {
                if (!ReadFile(_handle, pBuf, count, ref read, ov))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != ERROR_IO_PENDING)
                    {
                        boundHandle.FreeNativeOverlapped(ov);   // was _boundHandle
                        if (err == ERROR_OPERATION_ABORTED)
                            tcs.TrySetCanceled();
                        else
                            tcs.TrySetException(new Win32Exception(err));
                    }
                }
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
        var boundHandle = _boundHandle;   // capture once — free on THIS instance, never the field
        if (boundHandle != null && !boundHandle.Handle.IsClosed && !boundHandle.Handle.IsInvalid && _handle != null)
        {
            var ov = boundHandle.AllocateNativeOverlapped(
                (errorCode, numBytes, pOv) =>
                {
                    int mask = maskArr[0];
                    if (maskPin.IsAllocated) maskPin.Free();
                    try { boundHandle.FreeNativeOverlapped(pOv); }
                    catch (ObjectDisposedException) { }
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
                    boundHandle.FreeNativeOverlapped(ov);
                    if (err == ERROR_OPERATION_ABORTED)
                        tcs.TrySetCanceled();
                    else
                        tcs.TrySetException(new Win32Exception(err));
                }
            }
        }
        else
        {
            maskPin.Free();   // don't leak the pinned handle on the not-open path
            throw new InvalidOperationException("The port is not open");
        }
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

        _statTask = Task.Run(async () =>
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
        });

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

        // Fast-path guard: if we're somehow on a loop task, never join (that waits on
        // the current task). Just request stop + abort I/O; the owner's Close joins.
        // Best-effort only — see caveat below — but the timeout makes it non-fatal
        // even when this misses.
        int? cur = Task.CurrentId;
        if ((_readTask != null && _readTask.Id == cur) ||
            (_statTask != null && _statTask.Id == cur))
        {
            _closing = true;
            Thread.MemoryBarrier();
            try { if (_handle is { IsInvalid: false, IsClosed: false }) CancelIoEx(_handle, IntPtr.Zero); }
            catch (Win32Exception) { }
            return;
        }

        lock (_closeLock)                 // serialize concurrent/nested closes
        {
            if (!IsOpen) return;

            _closing = true;
            Thread.MemoryBarrier();

            try { if (_handle is { IsInvalid: false, IsClosed: false }) CancelIoEx(_handle, IntPtr.Zero); }
            catch (Win32Exception) { }

            var pending = new List<Task>(2);
            if (_readTask != null) pending.Add(_readTask);
            if (_statTask != null) pending.Add(_statTask);

            bool drained = true;
            if (pending.Count > 0)
            {
                try { drained = Task.WaitAll(pending.ToArray(), TimeSpan.FromSeconds(5)); }
                catch (AggregateException) { drained = true; }   // faulted still means the loop exited
            }

            if (drained)
            {
                // Every overlapped is freed; safe to dispose both handles in order.
                _boundHandle?.Dispose();
                _handle?.Dispose();
            }
            else
            {
                // A loop is wedged for an unknown reason and we refuse to hang.
                // Closing the FILE handle aborts any pending overlapped, so the loop
                // callbacks complete ABORTED and free their overlappeds against the
                // still-live bound handle. We deliberately do NOT dispose the bound
                // handle while an overlapped may be in flight: that is undefined
                // behavior (heap corruption, not a catchable exception). Leak it and
                // let finalization reclaim it after the handle close drains the I/O.
                Console.Error.WriteLine(
                    "[EspSerialSession] Close timed out waiting for I/O loops; forcing handle close.");
                try { _handle?.Dispose(); } catch { }
                try { Task.WaitAll(pending.ToArray(), TimeSpan.FromSeconds(2)); } catch { }  // grace for now-unblocked loops
            }

            _handle = null;
            _boundHandle = null;
            // _closing / _connErrorFired are intentionally NOT reset here — Open()
            // resets them (lines 706–707). Resetting now races a loop that is only
            // just observing _closing == true.
        }
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
        _closeLock = new object();
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

    const int ERROR_IO_PENDING = 0x000003E5;
    const uint ERROR_OPERATION_ABORTED = 995;
    const uint GENERIC_READ = 0x80000000;
    const uint GENERIC_WRITE = 0x40000000;
    const uint OPEN_EXISTING = 3;
    const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    const uint EV_RLSD = 0x0020;
    const uint DEVICE_NOTIFY_CALLBACK = 0x00000002;
    const uint PBT_APMSUSPEND = 0x0004;
    // GUID_DEVCLASS_PORTS {4d36e978-e325-11ce-bfc1-08002be10318}
    private static readonly Guid GUID_DEVCLASS_PORTS =
        new(0x4d36e978, 0xe325, 0x11ce, 0xbf, 0xc1, 0x08, 0x00, 0x2b, 0xe1, 0x03, 0x18);

    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DICS_FLAG_GLOBAL = 0x00000001;
    private const uint DIREG_DEV = 0x00000001;
    private const uint KEY_READ = 0x00020019;
    private const int ERROR_NO_MORE_ITEMS = 259;
    private const int ERROR_SUCCESS = 0;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nuint Reserved;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
    {
        public uint ReadIntervalTimeout;
        public uint ReadTotalTimeoutMultiplier;
        public uint ReadTotalTimeoutConstant;
        public uint WriteTotalTimeoutMultiplier;
        public uint WriteTotalTimeoutConstant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
    {
        public IntPtr Callback; // PDEVICE_NOTIFY_CALLBACK_ROUTINE (function pointer)
        public IntPtr Context;
    }
    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static unsafe partial IntPtr SetupDiGetClassDevsW(
       Guid* classGuid, char* enumerator, IntPtr hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, SP_DEVINFO_DATA* deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet, SP_DEVINFO_DATA* deviceInfoData,
        char* deviceInstanceId, uint deviceInstanceIdSize, uint* requiredSize);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    private static unsafe partial IntPtr SetupDiOpenDevRegKey(
        IntPtr deviceInfoSet, SP_DEVINFO_DATA* deviceInfoData,
        uint scope, uint hwProfile, uint keyType, uint samDesired);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [LibraryImport("advapi32.dll", SetLastError = false)]
    private static unsafe partial int RegQueryValueExW(
        IntPtr hKey, char* lpValueName, IntPtr lpReserved,
        uint* lpType, byte* lpData, uint* lpcbData);

    [LibraryImport("advapi32.dll")]
    private static partial int RegCloseKey(IntPtr hKey);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint DeviceNotifyCallbackRoutine(IntPtr context, uint type, IntPtr setting);

    #region kernel32
    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "CreateFileW",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCommMask(SafeFileHandle hFile, uint dwEvtMask);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClearCommError(SafeFileHandle hFile, ref int lpErrors, ref COMSTAT lpStat);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCommState(SafeFileHandle hFile, ref DCB lpDCB);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCommState(SafeFileHandle hFile, ref DCB lpDCB);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetupComm(SafeFileHandle hFile, int dwInQueue, int dwOutQueue);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCommTimeouts(SafeFileHandle hFile, ref COMMTIMEOUTS lpCommTimeouts);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WaitCommEvent(
        SafeFileHandle hFile, ref int lpEvtMask, NativeOverlapped* lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetOverlappedResult(
        SafeFileHandle hFile, NativeOverlapped* lpOverlapped,
        ref int lpNumberOfBytesTransferred, [MarshalAs(UnmanagedType.Bool)] bool bWait);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadFile(
        SafeFileHandle hFile, byte* lpBuffer, int nNumberOfBytesToRead,
        ref int lpNumberOfBytesRead, NativeOverlapped* lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool WriteFile(
        SafeFileHandle hFile, byte* lpBuffer, int nNumberOfBytesToWrite,
        ref int lpNumberOfBytesWritten, NativeOverlapped* lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);
    #endregion


    private unsafe void RegisterPowerNotification()
    {
        _powerCallbackHandle = GCHandle.Alloc(this);

        var sub = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            // Address of a static method — always valid, nothing to keep rooted for the OS.
            Callback = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, uint>)&PowerCallbackNative,
            Context = GCHandle.ToIntPtr(_powerCallbackHandle)
        };

        // Callback-based notification: no window or message pump required, so this works
        // identically in a WinUI3 app and a headless service.
        uint rc = PowerRegisterSuspendResumeNotification(
            DEVICE_NOTIFY_CALLBACK, ref sub, out _powerNotifyHandle);
        if (rc != 0)
        {
            _powerCallbackHandle.Free();
            _powerNotifyHandle = IntPtr.Zero;
            throw new Win32Exception((int)rc);
        }
    }

    private void UnregisterPowerNotification()
    {
        var h = Interlocked.Exchange(ref _powerNotifyHandle, IntPtr.Zero);
        if (h != IntPtr.Zero)
        {
            // Blocks until any in-progress callback returns, so after this the GCHandle
            // is safe to free. Must NOT be called from inside the callback itself
            // (would deadlock) — see OnSuspend. Only the thread that won the exchange
            // reaches here, so the handle is freed exactly once.
            try { PowerUnregisterSuspendResumeNotification(h); }
            catch (Win32Exception) { }

            if (_powerCallbackHandle.IsAllocated)
                _powerCallbackHandle.Free();
        }
    }

    // Runs on an OS power-management thread. Must return promptly, and must never let a
    // managed exception escape to native code — under NativeAOT that fastfails the process.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint PowerCallbackNative(IntPtr context, uint type, IntPtr setting)
    {
        try
        {
            if (type == PBT_APMSUSPEND && context != IntPtr.Zero)
            {
                var self = GCHandle.FromIntPtr(context).Target as EspSerialSession;
                self?.OnSuspend();
            }
        }
        catch
        {
            // Never propagate across the native boundary.
        }
        return 0; // ERROR_SUCCESS
    }
    #region powrprof
    [LibraryImport("powrprof.dll", SetLastError = false)]
    private static partial uint PowerRegisterSuspendResumeNotification(
        uint flags, ref DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS recipient, out IntPtr registrationHandle);

    [LibraryImport("powrprof.dll", SetLastError = false)]
    private static partial uint PowerUnregisterSuspendResumeNotification(IntPtr registrationHandle);
    #endregion
    

}
#pragma warning restore