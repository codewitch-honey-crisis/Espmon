using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Espmon;

internal static partial class WindowsServiceManager
{
    // Hardcoded service identity — matches what the Worker Service registers as.
    private const string ServiceName = "Espmon Service";
    private const string ServiceDisplayName = "Espmon Service";
    private const string ServiceDescription = "Espmon background hardware monitoring service";

    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    // =========================================================================
    // Win32 constants
    // =========================================================================

    // SCM access rights
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SC_MANAGER_CREATE_SERVICE = 0x0002;

    // Service access rights
    private const uint SERVICE_QUERY_STATUS = 0x0004;
    private const uint SERVICE_START = 0x0010;
    private const uint SERVICE_STOP = 0x0020;
    private const uint DELETE = 0x10000;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;

    // Service type
    private const uint SERVICE_WIN32_OWN_PROCESS = 0x00000010;

    // Start type
    private const uint SERVICE_AUTO_START = 0x00000002;

    // Error control
    private const uint SERVICE_ERROR_NORMAL = 0x00000001;

    // Service control codes
    private const uint SERVICE_CONTROL_STOP = 0x00000001;

    // Service state values (from winsvc.h)
    private const uint SERVICE_STOPPED = 0x00000001;
    private const uint SERVICE_START_PENDING = 0x00000002;
    private const uint SERVICE_STOP_PENDING = 0x00000003;
    private const uint SERVICE_RUNNING = 0x00000004;
    private const uint SERVICE_CONTINUE_PENDING = 0x00000005;
    private const uint SERVICE_PAUSE_PENDING = 0x00000006;
    private const uint SERVICE_PAUSED = 0x00000007;

    // Error codes
    private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;
    private const int ERROR_ACCESS_DENIED = 5;

    // =========================================================================
    // Win32 structs
    // =========================================================================

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SERVICE_DESCRIPTION_STRUCT
    {
        public nint lpDescription;
    }

    // =========================================================================
    // P/Invoke declarations (LibraryImport — .NET 7+ source generator)
    // =========================================================================

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "CreateServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateService(
        nint hSCManager,
        string lpServiceName,
        string lpDisplayName,
        uint dwDesiredAccess,
        uint dwServiceType,
        uint dwStartType,
        uint dwErrorControl,
        string lpBinaryPathName,
        string? lpLoadOrderGroup,
        nint lpdwTagId,
        string? lpDependencies,
        string? lpServiceStartName,
        string? lpPassword);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenService(nint hSCManager, string lpServiceName, uint dwDesiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "DeleteService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(nint hService);

    [LibraryImport("advapi32.dll", EntryPoint = "StartServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartService(nint hService, uint dwNumServiceArgs, nint lpServiceArgVectors);

    [LibraryImport("advapi32.dll", EntryPoint = "ControlService", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(nint hService, uint dwControl, out SERVICE_STATUS lpServiceStatus);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceStatus", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(nint hService, out SERVICE_STATUS lpServiceStatus);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig2(nint hService, uint dwInfoLevel, ref SERVICE_DESCRIPTION_STRUCT lpInfo);

    [LibraryImport("advapi32.dll", EntryPoint = "CloseServiceHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint hSCObject);

    private const uint SERVICE_CONFIG_DESCRIPTION = 1;

    // =========================================================================
    // Safe handle helper
    // =========================================================================

    /// <summary>
    /// RAII wrapper so we never leak SCM / service handles, even on exceptions.
    /// </summary>
    private readonly struct ScHandle : IDisposable
    {
        public nint Value { get; }
        public bool IsInvalid => Value == 0;

        public ScHandle(nint value) => Value = value;

        public void Dispose()
        {
            if (Value != 0)
                CloseServiceHandle(Value);
        }
    }

    // =========================================================================
    // Public API — same signatures as the original sc.exe version
    // =========================================================================

    // -------------------------------------------------------------------------
    // Install
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers the service with the SCM via P/Invoke.
    /// <paramref name="exePath"/> should be the fully-qualified path to the
    /// Worker Service executable.
    /// </summary>
    public static async Task InstallAsync(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("A valid executable path is required.", nameof(exePath));

        if (!File.Exists(exePath))
            throw new FileNotFoundException("Service executable not found.", exePath);

        if (IsInstalled)
            throw new InvalidOperationException($"Service '{ServiceName}' is already installed.");

        using var scm = OpenScManager(SC_MANAGER_CREATE_SERVICE);

        using var svc = new ScHandle(CreateService(
            scm.Value,
            ServiceName,
            ServiceDisplayName,
            SERVICE_ALL_ACCESS,
            SERVICE_WIN32_OWN_PROCESS,
            SERVICE_AUTO_START,
            SERVICE_ERROR_NORMAL,
            exePath,
            null,   // load-order group
            0,      // tag id
            null,   // dependencies
            null,   // LocalSystem (default)
            null)); // no password

        if (svc.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // Set description — CreateService doesn't support it directly.
        SetDescription(svc.Value, ServiceDescription);
    }

    // -------------------------------------------------------------------------
    // Uninstall
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops the service if running, then removes it from the SCM.
    /// </summary>
    public static async Task UninstallAsync()
    {
        if (!IsInstalled)
            throw new InvalidOperationException($"Service '{ServiceName}' is not installed.");

        // Stop first so the delete takes effect immediately.
        await StopAsync();

        using var scm = OpenScManager(SC_MANAGER_CONNECT);
        using var svc = OpenServiceHandle(scm.Value, DELETE);

        if (!DeleteService(svc.Value))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    // -------------------------------------------------------------------------
    // Start
    // -------------------------------------------------------------------------

    public static async Task StartAsync()
    {
        var state = QueryState();

        if (state == ServiceState.Running) return;

        if (state != ServiceState.StartPending)
        {
            using var scm = OpenScManager(SC_MANAGER_CONNECT);
            using var svc = OpenServiceHandle(scm.Value, SERVICE_START | SERVICE_QUERY_STATUS);

            if (!StartService(svc.Value, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        await WaitForStateAsync(ServiceState.Running, StatusTimeout);
    }

    // -------------------------------------------------------------------------
    // Stop
    // -------------------------------------------------------------------------

    public static async Task StopAsync()
    {
        var state = QueryState();

        if (state is ServiceState.Stopped or ServiceState.StopPending)
        {
            if (state == ServiceState.StopPending)
                await WaitForStateAsync(ServiceState.Stopped, StatusTimeout);
            return;
        }

        using var scm = OpenScManager(SC_MANAGER_CONNECT);
        using var svc = OpenServiceHandle(scm.Value, SERVICE_STOP | SERVICE_QUERY_STATUS);

        if (!ControlService(svc.Value, SERVICE_CONTROL_STOP, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        await WaitForStateAsync(ServiceState.Stopped, StatusTimeout);
    }

    // -------------------------------------------------------------------------
    // Status  (no admin required — uses SC_MANAGER_CONNECT + SERVICE_QUERY_STATUS)
    // -------------------------------------------------------------------------

    public static bool IsInstalled
    {
        get
        {

            using var scm = new ScHandle(OpenSCManager(null, null, SC_MANAGER_CONNECT));
            if (scm.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            using var svc = new ScHandle(OpenService(scm.Value, ServiceName, SERVICE_QUERY_STATUS));

            if (!svc.IsInvalid)
                return true;

            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_SERVICE_DOES_NOT_EXIST)
                return false;

            // ACCESS_DENIED means the service exists but we can't open it with
            // this access level — still counts as "installed".
            if (err == ERROR_ACCESS_DENIED)
                return true;

            throw new Win32Exception(err);
        }
    }

    public static bool IsStarted
    {
        get
        {
            try
            {
                var state = QueryState();
                return state == ServiceState.Running;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private enum ServiceState
    {
        Stopped,
        StartPending,
        StopPending,
        Running,
        ContinuePending,
        PausePending,
        Paused,
        Unknown,
    }

    /// <summary>
    /// Opens the SCM with the requested access rights.
    /// </summary>
    private static ScHandle OpenScManager(uint access)
    {
        var handle = new ScHandle(OpenSCManager(null, null, access));
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return handle;
    }

    /// <summary>
    /// Opens the service with the requested access rights.
    /// Throws <see cref="InvalidOperationException"/> if the service isn't installed.
    /// </summary>
    private static ScHandle OpenServiceHandle(nint hScm, uint access)
    {
        var handle = new ScHandle(OpenService(hScm, ServiceName, access));
        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            if (err == ERROR_SERVICE_DOES_NOT_EXIST)
                throw new InvalidOperationException($"Service '{ServiceName}' is not installed.");
            throw new Win32Exception(err);
        }
        return handle;
    }

    /// <summary>
    /// Queries the current service state via <c>QueryServiceStatus</c>.
    /// </summary>
    private static ServiceState QueryState()
    {

        using var scm = OpenScManager(SC_MANAGER_CONNECT);
        using var svc = OpenServiceHandle(scm.Value, SERVICE_QUERY_STATUS);
        SERVICE_STATUS status = default;
        if (!QueryServiceStatus(svc.Value, out status))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return status.dwCurrentState switch
        {
            SERVICE_STOPPED => ServiceState.Stopped,
            SERVICE_START_PENDING => ServiceState.StartPending,
            SERVICE_STOP_PENDING => ServiceState.StopPending,
            SERVICE_RUNNING => ServiceState.Running,
            SERVICE_CONTINUE_PENDING => ServiceState.ContinuePending,
            SERVICE_PAUSE_PENDING => ServiceState.PausePending,
            SERVICE_PAUSED => ServiceState.Paused,
            _ => ServiceState.Unknown,
        };
    }

    /// <summary>
    /// Polls until the service reaches the desired state or the timeout expires.
    /// </summary>
    private static async Task WaitForStateAsync(ServiceState desired, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + timeout.Ticks;

        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (QueryState() == desired) return;
            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"Service '{ServiceName}' did not reach state '{desired}' within {timeout.TotalSeconds}s.");
    }

    /// <summary>
    /// Sets the service description via <c>ChangeServiceConfig2</c>.
    /// </summary>
    private static void SetDescription(nint hService, string description)
    {
        nint pDesc = Marshal.StringToHGlobalUni(description);
        try
        {
            var desc = new SERVICE_DESCRIPTION_STRUCT { lpDescription = pDesc };
            if (!ChangeServiceConfig2(hService, SERVICE_CONFIG_DESCRIPTION, ref desc))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeHGlobal(pDesc);
        }
    }
}