using System;
using System.IO;
using System.Runtime.InteropServices;

// Espmon uninstaller.
//
// Same principles as the installer: raw Win32 P/Invoke, AOT-safe, message-box driven,
// and the installer's reboot-prompt pattern.
//
// The trick: an exe can't delete the folder it's running from, so a stale Program Files
// entry would linger until reboot. To avoid that, on first launch this process copies
// itself into a temp staging folder and relaunches from there, then exits. The temp copy
// waits for the original to exit (unlocking its exe), then does the real work: stop +
// delete the service, delete the entire install directory, and -- for anything still
// locked -- schedule it for deletion on reboot. Only genuinely-locked *install* files set
// the reboot flag; the temp copy schedules its own removal on reboot silently, since that
// leftover is harmless and clears at the next boot regardless.
//
// Build notes (outside this file):
//   * Ship with a requireAdministrator manifest, like the installer -- SCM control and
//     HKLM PendingFileRenameOperations both need elevation.
//   * Compile as WinExe (OutputType) so there's no console-window flash; all UI is
//     message boxes with a NULL owner.
internal static unsafe partial class Program
{
    // ---- Customize to match your app --------------------------------------
    private const string AppName = "Espmon";
    // May be either the service *key* name or its *display* name -- we resolve either one.
    private const string ServiceName = "Espmon Service";
    // Marker files that must BOTH be present for a folder to be treated as a real Espmon
    // install. Guards against a moved/misplaced uninstaller recursively deleting an
    // unrelated folder.
    private const string MainExeName = "Espmon.exe";
    private const string ServiceExeName = "Espmon.Service.exe";
    // -----------------------------------------------------------------------

    // Set when any *install-directory* file/dir had to be deferred to reboot deletion.
    // Drives the end-of-uninstall reboot prompt. The temp-copy self-cleanup does NOT set
    // this (see ScheduleSelfCleanup).
    private static bool _rebootNeeded;

    private static int Main(string[] args)
    {
        try
        {
            // The relocated worker is launched with two args: [installDir, parentPid].
            // A bare launch (no args) is the original instance.
            if (args.Length >= 2)
                RunWorker(args);
            else
                RunFirstInstance();
        }
        catch (Exception ex)
        {
            MessageBoxW(0, "Uninstall error:\n" + ex.Message, AppName, MB_ICONERROR);
        }
        return 0;
    }

    // ---- First instance: confirm, relocate to temp, relaunch, exit ---------
    private static void RunFirstInstance()
    {
        string ownExe = GetOwnExePath();
        // The uninstaller ships inside the install directory, so its own folder *is* the
        // directory to remove. Captured here and handed to the worker.
        string installDir = Path.GetDirectoryName(ownExe) ?? "";
        if (installDir.Length == 0)
            throw new InvalidOperationException("Could not determine the install directory.");

        // Safety: only proceed if this really looks like an Espmon install. Prevents a
        // moved/misplaced uninstaller from recursively deleting an unrelated folder.
        if (!LooksLikeEspmonInstall(installDir))
        {
            MessageBoxW(0,
                "This doesn't look like a valid " + AppName + " installation folder -- " +
                MainExeName + " and/or " + ServiceExeName + " is missing. The install may be " +
                "corrupt, or the uninstaller was moved.\n\nNothing has been removed.",
                AppName, MB_ICONERROR);
            return;
        }

        if (MessageBoxW(0,
                "This will uninstall " + AppName + " and remove its files. Continue?",
                AppName, MB_ICONWARNING | MB_YESNO) != IDYES)
            return;

        // Copy self into a fresh temp staging folder and relaunch from there.
        string staging = Path.Combine(
            Path.GetTempPath(),
            AppName + "_Uninstall_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(staging);
        string stagedExe = Path.Combine(staging, Path.GetFileName(ownExe));
        File.Copy(ownExe, stagedExe, overwrite: true);

        LaunchWorker(stagedExe, installDir, (int)GetCurrentProcessId());
        // Returning exits this process, which unlocks ownExe so the worker can delete it.
    }

    // ---- Worker (running from temp): do the actual removal ------------------
    private static void RunWorker(string[] args)
    {
        string installDir = args[0];
        _ = int.TryParse(args[1], out int parentPid);

        // Defense-in-depth: the worker is what actually stops the service and deletes files,
        // so re-verify before touching anything -- even though the first instance validated.
        if (!LooksLikeEspmonInstall(installDir))
        {
            MessageBoxW(0,
                "Aborting uninstall: \"" + installDir + "\" is not a valid " + AppName +
                " installation (" + MainExeName + " and/or " + ServiceExeName +
                " is missing). Nothing has been removed.",
                AppName, MB_ICONERROR);
            return;
        }

        // Wait for the original to exit so its exe (in installDir) is no longer locked.
        WaitForProcessExit(parentPid, 15_000);

        // Best effort: a missing/already-stopped service must not abort the file removal.
        try { ServiceCleanup(); } catch { /* ignore -- proceed to delete files */ }

        DeleteInstallDirectory(installDir);

        // Our own running exe (in temp) can't be deleted now -- schedule it for reboot.
        ScheduleSelfCleanup();

        MessageBoxW(0, AppName + " has been uninstalled.", AppName, MB_ICONINFORMATION);
        MaybePromptReboot(0);
    }

    // ---- Service: stop then delete (best effort) ---------------------------
    private static void ServiceCleanup()
    {
        nint scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == 0) return; // no SCM access (e.g. not elevated) -> skip
        try
        {
            nint svc = OpenServiceHandle(scm, ServiceName, SERVICE_QUERY_STATUS | SERVICE_STOP | DELETE);
            if (svc == 0) return; // not installed
            try
            {
                StopServiceHandle(svc);
                DeleteService(svc); // ignore result; a lingering marked-for-delete entry is acceptable
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    private static void StopServiceHandle(nint svc)
    {
        var st = default(SERVICE_STATUS);
        if (!QueryServiceStatus(svc, &st)) return;
        if (st.dwCurrentState == SERVICE_STOPPED) return;

        if (st.dwCurrentState != SERVICE_STOP_PENDING)
        {
            var c = default(SERVICE_STATUS);
            ControlService(svc, SERVICE_CONTROL_STOP, &c); // best effort
        }
        WaitForState(svc, SERVICE_STOPPED, 30_000); // best effort; don't hard-fail
    }

    // True only if both marker exes are present -- the signal that this folder is a real
    // Espmon install and safe to remove.
    private static bool LooksLikeEspmonInstall(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, MainExeName))
                && File.Exists(Path.Combine(dir, ServiceExeName));
        }
        catch { return false; }
    }

    // ---- Install-directory removal (with reboot fallback) ------------------
    private static void DeleteInstallDirectory(string installDir)
    {
        string root;
        try { root = Path.GetFullPath(installDir); }
        catch { return; }
        if (!Directory.Exists(root)) return;

        // Guard: never recursively delete a drive root (e.g. "C:\").
        string? drive = Path.GetPathRoot(root);
        if (drive is not null &&
            drive.TrimEnd('\\', '/').Equals(root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete a drive root: " + root);

        // Files first, so directories are empty when we reach them (and so reboot-pending
        // renames are ordered files-before-dirs, which is how they're processed at boot).
        foreach (string file in SafeFiles(root))
            DeleteFileOrDefer(file);

        // Then directories deepest-first, and finally the root itself.
        foreach (string dir in SafeDirsDeepestFirst(root))
            DeleteDirOrDefer(dir);
        DeleteDirOrDefer(root);
    }

    private static void DeleteFileOrDefer(string file)
    {
        try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best effort */ }
        try { File.Delete(file); return; } catch { /* locked -> defer below */ }

        if (MoveFileExW(file, null, MOVEFILE_DELAY_UNTIL_REBOOT))
            _rebootNeeded = true;
    }

    private static void DeleteDirOrDefer(string dir)
    {
        try { Directory.Delete(dir, recursive: false); return; } catch { /* not empty / locked */ }

        if (MoveFileExW(dir, null, MOVEFILE_DELAY_UNTIL_REBOOT))
            _rebootNeeded = true;
    }

    // Removes our own temp exe + staging folder on reboot. Deliberately does NOT set
    // _rebootNeeded: these are harmless temp leftovers that Windows clears at the next
    // reboot whether or not the user reboots now, so they shouldn't trigger a prompt.
    private static void ScheduleSelfCleanup()
    {
        string ownExe = GetOwnExePath();
        MoveFileExW(ownExe, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        string? ownDir = Path.GetDirectoryName(ownExe);
        if (!string.IsNullOrEmpty(ownDir))
            MoveFileExW(ownDir, null, MOVEFILE_DELAY_UNTIL_REBOOT);
    }

    private static string[] SafeFiles(string root)
    {
        try { return Directory.GetFiles(root, "*", SearchOption.AllDirectories); }
        catch { return Array.Empty<string>(); }
    }

    private static string[] SafeDirsDeepestFirst(string root)
    {
        try
        {
            string[] dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
            // Longer path == deeper, so this deletes children before their parents.
            Array.Sort(dirs, static (a, b) => b.Length.CompareTo(a.Length));
            return dirs;
        }
        catch { return Array.Empty<string>(); }
    }

    // ---- Relaunch plumbing -------------------------------------------------
    private static string GetOwnExePath()
    {
        char* buf = stackalloc char[1024];
        uint n = GetModuleFileNameW(0, buf, 1024);
        return new string(buf, 0, (int)n);
    }

    private static void LaunchWorker(string exePath, string installDir, int pid)
    {
        // argv: "<exe>" "<installDir>" <pid>
        string cmd = "\"" + exePath + "\" \"" + installDir + "\" " + pid;

        // CreateProcessW may write to lpCommandLine, so hand it a writable, null-terminated
        // buffer rather than a pinned string literal.
        char[] buf = new char[cmd.Length + 1];
        cmd.CopyTo(0, buf, 0, cmd.Length);
        buf[cmd.Length] = '\0';

        var si = default(STARTUPINFOW);
        si.cb = (uint)sizeof(STARTUPINFOW);
        var pi = default(PROCESS_INFORMATION);

        fixed (char* pCmd = buf)
        {
            if (!CreateProcessW(exePath, pCmd, 0, 0, false, 0, 0, null, &si, &pi))
                throw new InvalidOperationException(
                    "Failed to relaunch the uninstaller from temp (error " +
                    Marshal.GetLastPInvokeError() + ").");
        }
        if (pi.hThread != 0) CloseHandle(pi.hThread);
        if (pi.hProcess != 0) CloseHandle(pi.hProcess);
    }

    private static void WaitForProcessExit(int pid, uint timeoutMs)
    {
        if (pid <= 0) return;
        nint h = OpenProcess(SYNCHRONIZE, false, (uint)pid);
        if (h == 0) return; // already gone
        try { WaitForSingleObject(h, timeoutMs); }
        finally { CloseHandle(h); }
    }

    // ---- Reboot prompt (mirrors the installer) -----------------------------
    private static void MaybePromptReboot(nint hWnd)
    {
        if (!_rebootNeeded) return;

        int r = MessageBoxW(hWnd,
            "Some files were in use and are scheduled to be removed when the system restarts. " +
            "It is recommended that the system be rebooted to finish removing " + AppName +
            ".\n\nWould you like to reboot now?",
            AppName, MB_ICONINFORMATION | MB_YESNO);

        if (r == IDYES)
            RebootSystem(hWnd);
    }

    // Graceful reboot: apps may block/delay it (no EWX_FORCE). Needs SeShutdownPrivilege.
    private static void RebootSystem(nint hWnd)
    {
        if (!EnableShutdownPrivilege())
        {
            MessageBoxW(hWnd,
                "Couldn't obtain permission to reboot. Please reboot manually to finish removing " +
                AppName + ".",
                AppName, MB_ICONWARNING);
            return;
        }

        if (!ExitWindowsEx(EWX_REBOOT, SHTDN_REASON_INSTALL))
            MessageBoxW(hWnd,
                "The reboot request failed (error " + Marshal.GetLastPInvokeError() +
                "). Please reboot manually to finish removing " + AppName + ".",
                AppName, MB_ICONWARNING);
    }

    private static bool EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out nint tok))
            return false;
        try
        {
            fixed (char* pName = "SeShutdownPrivilege")
            {
                if (!LookupPrivilegeValueW(null, pName, out LUID luid))
                    return false;

                var tp = new TOKEN_PRIVILEGES
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED,
                };
                if (!AdjustTokenPrivileges(tok, false, &tp, 0, null, null))
                    return false;

                // Succeeds even if the privilege wasn't held; the verdict is in last error.
                return Marshal.GetLastPInvokeError() == 0; // ERROR_NOT_ALL_ASSIGNED == 1300
            }
        }
        finally { CloseHandle(tok); }
    }

    // ---- Service name resolution (ported from the installer) ---------------
    private static nint OpenServiceHandle(nint scm, string name, uint access)
    {
        nint svc = OpenServiceW(scm, name, access);
        if (svc != 0) return svc;

        int err = Marshal.GetLastPInvokeError();
        if (err != ERROR_SERVICE_DOES_NOT_EXIST)
            throw new InvalidOperationException("OpenService(\"" + name + "\") failed (error " + err + ").");

        string? key = TryResolveServiceKey(scm, name); // maybe 'name' was a display name
        if (key is null || string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            return 0;

        svc = OpenServiceW(scm, key, access);
        if (svc == 0)
        {
            err = Marshal.GetLastPInvokeError();
            if (err == ERROR_SERVICE_DOES_NOT_EXIST) return 0;
            throw new InvalidOperationException("OpenService(\"" + key + "\") failed (error " + err + ").");
        }
        return svc;
    }

    private static string? TryResolveServiceKey(nint scm, string displayName)
    {
        uint cch = 256;
        char* buf = stackalloc char[256];
        if (GetServiceKeyNameW(scm, displayName, buf, ref cch))
            return new string(buf, 0, (int)cch);

        if (Marshal.GetLastPInvokeError() != ERROR_INSUFFICIENT_BUFFER)
            return null;

        char* big = stackalloc char[(int)cch + 1];
        uint cap = cch + 1;
        if (GetServiceKeyNameW(scm, displayName, big, ref cap))
            return new string(big, 0, (int)cap);
        return null;
    }

    private static bool WaitForState(nint svc, uint desired, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        var st = default(SERVICE_STATUS);
        while (true)
        {
            if (!QueryServiceStatus(svc, &st)) return false;
            if (st.dwCurrentState == desired) return true;
            if (Environment.TickCount64 >= deadline) return false;
            uint hint = st.dwWaitHint / 10;
            Sleep(hint < 250 ? 250 : hint > 1000 ? 1000 : hint);
        }
    }

    // ====================== constants =======================================
    private const uint MB_ICONERROR = 0x10, MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40;
    private const uint MB_YESNO = 0x00000004;
    private const int IDYES = 6;

    // Service Control Manager
    private const uint SC_MANAGER_CONNECT = 0x0001;
    private const uint SERVICE_QUERY_STATUS = 0x0004, SERVICE_STOP = 0x0020;
    private const uint DELETE = 0x00010000;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_STOPPED = 0x1, SERVICE_STOP_PENDING = 0x3;
    private const int ERROR_INSUFFICIENT_BUFFER = 122, ERROR_SERVICE_DOES_NOT_EXIST = 1060;

    // Process / file
    private const uint SYNCHRONIZE = 0x00100000;
    private const uint MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    // Reboot / shutdown privilege
    private const uint EWX_REBOOT = 0x00000002;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020, TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    // MAJOR_APPLICATION | MINOR_INSTALLATION | FLAG_PLANNED -- a clean, expected reboot.
    private const uint SHTDN_REASON_INSTALL = 0x00040000 | 0x00000002 | 0x80000000;

    // ====================== P/Invoke ========================================
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint GetModuleFileNameW(nint hModule, char* lpFileName, uint nSize);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint dwDesiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    private static partial void Sleep(uint dwMilliseconds);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileExW(string lpExistingFileName, string? lpNewFileName, uint dwFlags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(
        string? lpApplicationName, char* lpCommandLine, nint lpProcessAttributes,
        nint lpThreadAttributes, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags, nint lpEnvironment, string? lpCurrentDirectory,
        STARTUPINFOW* lpStartupInfo, PROCESS_INFORMATION* lpProcessInformation);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint h, string text, string caption, uint type);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExitWindowsEx(uint uFlags, uint dwReason);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint OpenServiceW(nint hSCManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetServiceKeyNameW(nint hSCManager, string displayName,
                                                   char* keyName, ref uint cchBuffer);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(nint hService, SERVICE_STATUS* status);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ControlService(nint hService, uint control, SERVICE_STATUS* status);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(nint hService);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint hSCObject);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValueW(char* lpSystemName, char* lpName, out LUID lpLuid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll, TOKEN_PRIVILEGES* newState,
        uint bufferLength, void* previousState, uint* returnLength);

    // ====================== structs =========================================
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

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    // TOKEN_PRIVILEGES flattened to a single privilege entry (PrivilegeCount is always 1).
    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOW
    {
        public uint cb;
        public char* lpReserved;
        public char* lpDesktop;
        public char* lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2;
        public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }
}
