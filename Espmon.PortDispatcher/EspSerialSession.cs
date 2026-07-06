#pragma warning disable CS0649
using Microsoft.Management.Infrastructure;
using Microsoft.Win32.SafeHandles;

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
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
[SupportedOSPlatform("windows")]
internal partial class EspSerialSession : IDisposable
{
    struct StateMachine
    {
        int state;
        byte rawCmd;
        int rawLen;
        uint rawCrc;
        public byte RawCommandByte => rawCmd;
        public byte Command => (byte)(rawCmd - 128);
        public int Length => Swap(rawLen);
        public uint Crc => Swap(rawCrc);
        public bool IsDone => state == 16;
        private int Swap(int value)
        {
            var x = unchecked((uint)value);
            return unchecked((int)Swap(x));
        }
        private uint Swap(uint x)
        {
            return (((x & 0x000000ff) << 24) +
                   ((x & 0x0000ff00) << 8) +
                   ((x & 0x00ff0000) >> 8) +
                   ((x & 0xff000000) >> 24));
        }
        public void Reset() => state = 0;
        public bool Step(List<byte>? log, object? logLock, byte data)
        {
            if (state == 0)
            {
                if (data < 128)
                {
                    if (logLock != null)
                    {
                        lock (logLock)
                        {
                            log?.Add(data);
                        }
                    }
                    return false;
                }
                state = 1;
                rawCmd = data;
                rawLen = 0;
            }
            else if (state < 8)
            {
                if (rawCmd != data)
                {
                    if (logLock != null)
                    {

                        lock (logLock)
                        {
                            for (var i = 0; i < state; ++i)
                            {
                                log?.Add(rawCmd);
                            }
                            log?.Add(data);
                        }
                    }
                    state = 0;
                    return false;
                }
                ++state;
            }
            else if (state == 8)
            {
                rawLen = data;
                ++state;
            }
            else if (state < 12)
            {
                rawLen <<= 8;
                rawLen |= data;
                ++state;
            }
            else if (state == 12)
            {
                rawCrc = data;
                ++state;
            }
            else if (state < 16)
            {
                rawCrc <<= 8;
                rawCrc |= data;
                ++state;
            }
            else if (state == 16)
            {
                state = 0;
                return Step(log, logLock, data);
            }
            else
            {
                state = 0;
                return false;

            }
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
    DeviceNotifyCallbackRoutine? _powerCallback; // kept rooted: native code holds a pointer to it
    public event EventHandler<EventArgs>? ConnectionError;
    public event EventHandler<FrameReceivedEventArgs>? FrameReceived;
    public event EventHandler<FrameReceivedEventArgs>? FrameError;
    
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
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                Close();
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

    public void Send(byte cmd, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(cmd, 127, nameof(cmd));
        cmd += 128;
        Span<byte> len = [0, 0, 0, 0];
        BinaryPrimitives.WriteInt32LittleEndian(len, data.Length);
        Span<byte> crc = [0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32(data));
        Span<byte> toWrite = [cmd, cmd, cmd, cmd, cmd, cmd, cmd, cmd, .. len, .. crc, .. data];
        try
        {
            WriteAll(toWrite);
        }
        catch (Win32Exception)
        {
            OnConnectionError(EventArgs.Empty);
            Dispose(true);
        }
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
                    if (_closing || _boundHandle == null || _handle == null)
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

    private void OnFrameError(FrameReceivedEventArgs args)
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
    public void Open()
    {
        if (IsOpen) return;
        _closing = false;
        _connErrorFired = false;
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
                        if (mach.Length > 0)
                        {
                            if (mach.Length > 32768)
                            {
                                throw new IOException("Serial corruption detected in frame");
                            }
                            var frame = new byte[mach.Length];
                            await ReadExactlyAsync(frame, mach.Length);

                            if (Crc32(frame) == mach.Crc)
                            {
                                OnFrameReceived(new FrameReceivedEventArgs(mach.Command, frame));
                            }
                            else
                            {
                                OnFrameError(new FrameReceivedEventArgs(mach.Command, frame));
                            }
                        }
                        else
                        {
                            if (mach.Crc == UInt32.MaxValue / 3)
                            {
                                OnFrameReceived(new FrameReceivedEventArgs(mach.Command, Array.Empty<byte>()));
                            }
                            else
                            {
                                OnFrameError(new FrameReceivedEventArgs(mach.Command, Array.Empty<byte>()));
                            }

                        }
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
                        Task.WaitAll(tasks.AsSpan(0, taskCount));
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
    public EspSerialSession(string port, bool logging = false, SynchronizationContext? syncContext = null)
    {
        _logLock = new object();
        _ioLock = new object();
        _sync = syncContext;
        _log = new List<byte>();
        _portName = port;
        _logging = logging;

    }
    public bool IsLogging
    {
        get { return _logging; }
        set { _logging = value; }
    }
    static uint Crc32(ReadOnlySpan<byte> data, uint seed = uint.MaxValue / 3)
    {
        uint result = seed;
        int length = data.Length;
        int i = 0;
        while (length-- > 0)
        {
            result ^= data[i++];
        }
        return result;
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