using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

internal static unsafe partial class Program
{
    // ---- Customize to match your app --------------------------------------
    private const string AppName = "Espmon";
    // Path of the launchable exe *within the archive's root folder*. The zip is laid
    // out as  Espmon\Espmon.exe , and since we strip the archive root on extract, the
    // exe ends up directly under the chosen install root -> just the file name here.
    private const string ExeUnderRoot = "Espmon.exe";
    private const string ResourceSuffix = "Espmon.zip";
    // The Windows service to stop before extraction and restart afterward. This may be
    // either the service *key* name or its *display* name -- we resolve either one.
    private const string ServiceName = "Espmon Service";
    // Canonical identity used whenever the installer (re)creates the service itself, so it
    // matches what the Worker Service registers as.
    private const string ServiceDisplayName = "Espmon Service";
    private const string ServiceDescription = "Espmon background hardware monitoring service";
    // The service's executable file name. If the registered service points at any other
    // exe, we treat it as "not ours" and leave it untouched.
    private const string ServiceExeName = "Espmon.Service.exe";
    // Written into the install directory whenever the installer (re)creates the service
    // itself, so the service can locate the per-user Espmon data folder. Same name and
    // shape as the config the app ships.
    private const string ServiceConfigFileName = "espmon.service.config.json";
    // -----------------------------------------------------------------------

    // Set when the installer deletes and/or recreates the service during this run. A
    // deleted service stays "marked for deletion" until reboot and a freshly (re)created
    // one may not surface until reboot -- so these drive the end-of-install reboot prompt
    // (_serviceDeleted) and the config-file write (_serviceRecreated).
    private static bool _rebootNeeded;
    private static bool _serviceRecreated;

    private const int ID_EDIT = 101, ID_BROWSE = 102, ID_DESKTOP = 103,
                      ID_STARTMENU = 104, ID_INSTALL = 105;

    private static nint _hEdit, _hDesktop, _hStartMenu, _hFont;
    private static readonly StrategyBasedComWrappers Com = new();

    [STAThread]
    private static int Main()
    {
        CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        nint hInstance = GetModuleHandleW(null);

        const string clsName = "EspmonInstallerWindow";
        fixed (char* pCls = clsName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                hInstance = hInstance,
                hCursor = LoadCursorW(0, (char*)IDC_ARROW),
                hbrBackground = (nint)(COLOR_BTNFACE + 1),
            };
            wc.lpfnWndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc;
            wc.lpszClassName = pCls;
            RegisterClassExW(&wc);
        }

        _hFont = GetStockObject(DEFAULT_GUI_FONT);

        nint hWnd = CreateWindowExW(
            0, clsName, AppName + " Installer",
            WS_OVERLAPPEDWINDOW & ~WS_THICKFRAME & ~WS_MAXIMIZEBOX,
            CW_USEDEFAULT, CW_USEDEFAULT, 452, 248,
            0, 0, hInstance, null);

        ShowWindow(hWnd, SW_SHOW);
        UpdateWindow(hWnd);

        MSG msg;
        while (GetMessageW(&msg, 0, 0, 0) > 0)
        {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }

        CoUninitialize();
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WM_CREATE:
                CreateControls(hWnd);
                return 0;
            case WM_COMMAND:
                int id = (int)(wParam & 0xFFFF);
                if (id == ID_BROWSE) OnBrowse(hWnd);
                else if (id == ID_INSTALL) OnInstall(hWnd);
                return 0;
            case WM_CLOSE:
                DestroyWindow(hWnd);
                return 0;
            case WM_DESTROY:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private static void CreateControls(nint hWnd)
    {
        nint hi = GetModuleHandleW(null);
        Child(hWnd, "STATIC", "Install location:", SS_LEFT, 16, 14, 410, 18, 0, hi);

        string def = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);
        _hEdit = Child(hWnd, "EDIT", def, WS_BORDER | ES_AUTOHSCROLL | WS_TABSTOP,
                       16, 36, 320, 24, ID_EDIT, hi);
        Child(hWnd, "BUTTON", "Browse...", BS_PUSHBUTTON | WS_TABSTOP,
              344, 36, 84, 24, ID_BROWSE, hi);

        _hDesktop = Child(hWnd, "BUTTON", "Create desktop shortcut",
                          BS_AUTOCHECKBOX | WS_TABSTOP, 16, 80, 410, 22, ID_DESKTOP, hi);
        _hStartMenu = Child(hWnd, "BUTTON", "Create Start Menu shortcut",
                            BS_AUTOCHECKBOX | WS_TABSTOP, 16, 106, 410, 22, ID_STARTMENU, hi);

        Child(hWnd, "BUTTON", "Install", BS_DEFPUSHBUTTON | WS_TABSTOP,
              328, 158, 100, 30, ID_INSTALL, hi);
    }

    private static nint Child(nint parent, string cls, string text, uint style,
                              int x, int y, int w, int h, int id, nint hi)
    {
        nint c = CreateWindowExW(0, cls, text, WS_CHILD | WS_VISIBLE | style,
                                 x, y, w, h, parent, id, hi, null);
        SendMessageW(c, WM_SETFONT, _hFont, 1);
        return c;
    }

    private static void OnBrowse(nint hWnd)
    {
        char* display = stackalloc char[260];
        fixed (char* title = "Select the base folder")
        {
            var bi = new BROWSEINFOW
            {
                hwndOwner = hWnd,
                pszDisplayName = display,
                lpszTitle = title,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE,
            };
            nint pidl = SHBrowseForFolderW(&bi);
            if (pidl != 0)
            {
                char* path = stackalloc char[260];
                if (SHGetPathFromIDListW(pidl, path))
                {
                    // Browse selects the *base* folder; keep the current install-folder
                    // name as the last segment so the user's chosen root name sticks.
                    // (If they browsed straight into a folder already named that, don't
                    // double it up.)
                    string baseDir = new string(path);
                    string leaf = LeafOf(GetText(_hEdit));
                    string full = baseDir.TrimEnd('\\', '/').EndsWith(leaf, StringComparison.OrdinalIgnoreCase)
                        ? baseDir
                        : Path.Combine(baseDir, leaf);
                    SetWindowTextW(_hEdit, full);
                }
                CoTaskMemFree(pidl);
            }
        }
    }

    private static void OnInstall(nint hWnd)
    {
        bool weStoppedService = false;
        _rebootNeeded = false;
        _serviceRecreated = false;
        try
        {
            string target = GetText(_hEdit);
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBoxW(hWnd, "Please choose an install location.", AppName, MB_ICONWARNING);
                return;
            }

            // Stop the service first: its binary is part of the payload and will be
            // overwritten at 'target', so it must not be running (files would be locked).
            // If it won't stop in time this throws -> we abort before touching any files.
            weStoppedService = StopServiceIfRunning();
            if (!ClearInstallDir(hWnd, target))
            {
                MaybePromptReboot(hWnd);
                DestroyWindow(hWnd);
                return;
            }
            ExtractEmbeddedZip(target);

            string exe = Path.Combine(target, ExeUnderRoot);
            if (IsChecked(_hDesktop))
            {
                string d = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(Path.Combine(d, AppName + ".lnk"), exe);
            }
            if (IsChecked(_hStartMenu))
            {
                string p = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                string dir = Path.Combine(p, AppName);
                Directory.CreateDirectory(dir);
                CreateShortcut(Path.Combine(dir, AppName + ".lnk"), exe);
            }

            // Two independent concerns, both handled below:
            //   1. Re-registration: if the service is registered to an exe outside this
            //      install tree, delete + recreate it pointing at the freshly-extracted
            //      binary (preserving the rest of its config). Runs whenever it's pointing
            //      at the wrong folder -- even if the service was already stopped.
            //   2. Restart: only if *we* stopped a running service.
            // Files are already in place here, so failures aren't fatal to the install --
            // surface a warning with the correct exe path and let the user finish manually.
            try
            {
                RelocateServiceIfNeeded(target);
                if (FindServiceExe(target) is not null)   // payload ships the service -> its config was wiped, rewrite it
                    WriteServiceConfig(target);
                if (weStoppedService)
                    StartService();
            }
            catch (Exception svcEx)
            {
                MessageBoxW(hWnd,
                    AppName + " was installed to:\n" + target +
                    "\n\nBut the \"" + ServiceName + "\" service could not be updated automatically:\n" +
                    svcEx.Message + "\n\nCheck it in services.msc; it should point at:\n" +
                    Path.Combine(target, ServiceExeName),
                    AppName, MB_ICONWARNING);
                MaybePromptReboot(hWnd); // a delete may have gone through even if recreate/start didn't
                DestroyWindow(hWnd);
                return;
            }

            MessageBoxW(hWnd, AppName + " was installed to:\n" + target, AppName, MB_ICONINFORMATION);
            MaybePromptReboot(hWnd);
            DestroyWindow(hWnd); // success: tear down -> WM_DESTROY -> PostQuitMessage -> exit
        }
        catch (Exception ex)
        {
            MessageBoxW(hWnd, "Install failed:\n" + ex.Message, AppName, MB_ICONERROR);
        }
    }

    // ---- Windows service control (raw SCM via advapi32, AOT-safe) ----------
    // Returns true only if this call actually transitioned a running service to STOPPED,
    // i.e. the installer is responsible for restarting it. Returns false if the service
    // isn't installed or was already stopped. Throws (aborting the install) if a running
    // service refuses to stop within the timeout.
    private static bool StopServiceIfRunning()
    {
        nint scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == 0)
            throw new InvalidOperationException(
                "Unable to open the Service Control Manager (error " + Marshal.GetLastPInvokeError() +
                "). The installer must run elevated.");
        try
        {
            nint svc = OpenServiceHandle(scm, ServiceName, SERVICE_QUERY_STATUS | SERVICE_STOP);
            if (svc == 0) return false; // service not installed -> nothing to stop
            try
            {
                var st = default(SERVICE_STATUS);
                if (!QueryServiceStatus(svc, &st))
                    throw new InvalidOperationException(
                        "QueryServiceStatus failed (error " + Marshal.GetLastPInvokeError() + ").");

                if (st.dwCurrentState == SERVICE_STOPPED)
                    return false; // already stopped -> not ours to restart

                // If a stop isn't already in progress, ask for one.
                if (st.dwCurrentState != SERVICE_STOP_PENDING)
                {
                    var ctl = default(SERVICE_STATUS);
                    if (!ControlService(svc, SERVICE_CONTROL_STOP, &ctl))
                    {
                        int err = Marshal.GetLastPInvokeError();
                        if (err == ERROR_SERVICE_NOT_ACTIVE) return false;
                        throw new InvalidOperationException(
                            "Failed to send STOP to \"" + ServiceName + "\" (error " + err + ").");
                    }
                }

                if (!WaitForState(svc, SERVICE_STOPPED, 30_000))
                    throw new TimeoutException(
                        "The \"" + ServiceName + "\" service did not stop within 30 seconds. " +
                        "Aborting so its files aren't overwritten while in use.");

                return true; // we transitioned it to stopped
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    private static void StartService()
    {
        nint scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == 0)
            throw new InvalidOperationException(
                "Unable to open the Service Control Manager (error " + Marshal.GetLastPInvokeError() + ").");
        try
        {
            nint svc = OpenServiceHandle(scm, ServiceName, SERVICE_QUERY_STATUS | SERVICE_START);
            if (svc == 0)
                throw new InvalidOperationException("The \"" + ServiceName + "\" service is no longer present.");
            try
            {
                if (!StartServiceW(svc, 0, null))
                {
                    int err = Marshal.GetLastPInvokeError();
                    if (err != ERROR_SERVICE_ALREADY_RUNNING)
                        throw new InvalidOperationException(
                            "Failed to start \"" + ServiceName + "\" (error " + err + ").");
                }
                WaitForState(svc, SERVICE_RUNNING, 30_000); // best effort; don't hard-fail on the wait
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    // If the service is registered to an exe outside this install tree, delete it and
    // recreate it pointing at the freshly-extracted binary, preserving the rest of its
    // config. No-op if the service is absent, already correct, or not ours. Requires the
    // service to be stopped (it is by this point) so it can be deleted.
    private static void RelocateServiceIfNeeded(string installRoot)
    {
        string? newExe = FindServiceExe(installRoot);
        if (newExe is null) return; // payload doesn't contain the service exe -> nothing to point at

        nint scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
        if (scm == 0)
            throw new InvalidOperationException(
                "Unable to open the Service Control Manager (error " + Marshal.GetLastPInvokeError() + ").");
        try
        {
            nint svc = OpenServiceHandle(scm, ServiceName, SERVICE_QUERY_CONFIG | DELETE);
            if (svc == 0) return; // not installed -> nothing to relocate
            nint buf = 0;
            try
            {
                uint needed = 0;
                QueryServiceConfigW(svc, null, 0, &needed); // sizing call: expected to fail
                if (needed == 0)
                    throw new InvalidOperationException(
                        "QueryServiceConfig sizing failed (error " + Marshal.GetLastPInvokeError() + ").");

                buf = Marshal.AllocHGlobal((int)needed);
                var cfg = (QUERY_SERVICE_CONFIGW*)buf;
                if (!QueryServiceConfigW(svc, cfg, needed, &needed))
                    throw new InvalidOperationException(
                        "QueryServiceConfig failed (error " + Marshal.GetLastPInvokeError() + ").");

                (string oldExe, string argsTail) = SplitImagePath(ReadStr(cfg->lpBinaryPathName) ?? "");
                string oldExeFull = SafeFullPath(Environment.ExpandEnvironmentVariables(oldExe));

                // Ownership guard: if the registration points at some other exe, it isn't ours.
                if (!string.Equals(Path.GetFileName(oldExeFull), ServiceExeName, StringComparison.OrdinalIgnoreCase))
                    return;

                // Already pointing at the extracted binary -> nothing to do.
                if (PathEquals(oldExeFull, newExe)) return;

                // Recreating means re-supplying the account. Built-in accounts (LocalSystem /
                // LocalService / NetworkService) need no password; a custom account would, and
                // we don't have it -- so bail *before* deleting rather than destroy the service.
                string? account = ReadStr(cfg->lpServiceStartName);
                if (!IsBuiltinAccount(account))
                    throw new InvalidOperationException(
                        "The service runs under a custom account (" + account + "), which can't be " +
                        "recreated automatically. Repoint it manually at:\n" + newExe);

                uint serviceType = cfg->dwServiceType;
                uint startType = cfg->dwStartType;
                uint errorControl = cfg->dwErrorControl;
                string newBinaryPath = "\"" + newExe + "\"" + argsTail; // argsTail keeps its leading space

                if (!DeleteService(svc))
                    throw new InvalidOperationException(
                        "Failed to remove the old service registration (error " + Marshal.GetLastPInvokeError() + ").");
                _rebootNeeded = true; // now marked-for-delete until reboot -> warrants a reboot prompt
                CloseServiceHandle(svc);
                svc = 0;

                // Recreate with the canonical identity (name/display/description). Runtime
                // config (type, start mode, error control, account, args, load-order group,
                // dependencies) is preserved from the old registration.
                nint created = CreateServiceWithRetry(
                    scm, ServiceName, ServiceDisplayName, serviceType, startType, errorControl,
                    newBinaryPath, cfg->lpLoadOrderGroup, cfg->lpDependencies, account);
                _serviceRecreated = true; // the installer now owns this registration -> write its config
                try { SetServiceDescription(created, ServiceDescription); }
                finally { CloseServiceHandle(created); }
            }
            finally
            {
                if (svc != 0) CloseServiceHandle(svc);
                if (buf != 0) Marshal.FreeHGlobal(buf);
            }
        }
        finally { CloseServiceHandle(scm); }
    }

    // Recreate the service, retrying briefly while the old registration finishes deleting
    // (a freshly-deleted name can transiently report ERROR_SERVICE_MARKED_FOR_DELETE).
    private static nint CreateServiceWithRetry(nint scm, string name, string? display,
                                               uint type, uint start, uint errorControl,
                                               string binaryPath, char* loadGroup, char* deps,
                                               string? account)
    {
        for (int attempt = 0; ; attempt++)
        {
            nint h = CreateServiceW(scm, name, display, SERVICE_ALL_ACCESS, type, start, errorControl,
                                    binaryPath, loadGroup, null, deps, account, null);
            if (h != 0) return h;

            int err = Marshal.GetLastPInvokeError();
            if (err == ERROR_SERVICE_MARKED_FOR_DELETE && attempt < 20)
            {
                Sleep(300);
                continue;
            }
            throw new InvalidOperationException(
                "Failed to recreate the \"" + name + "\" service (error " + err + ").");
        }
    }

    // Best-effort: the description is cosmetic, so a failure here shouldn't fail the install.
    private static void SetServiceDescription(nint svc, string description)
    {
        fixed (char* p = description)
        {
            var d = new SERVICE_DESCRIPTION { lpDescription = p };
            ChangeServiceConfig2W(svc, SERVICE_CONFIG_DESCRIPTION, &d);
        }
    }

    // Locates ServiceExeName in the extracted tree (install root first, then anywhere below).
    private static string? FindServiceExe(string installRoot)
    {
        string direct = Path.Combine(installRoot, ServiceExeName);
        if (File.Exists(direct)) return SafeFullPath(direct);
        try
        {
            foreach (string f in Directory.EnumerateFiles(installRoot, ServiceExeName, SearchOption.AllDirectories))
                return SafeFullPath(f);
        }
        catch { /* ignore enumeration errors; treated as not found */ }
        return null;
    }

    // Splits a service ImagePath into (exe, argsTail) where argsTail keeps its leading space.
    private static (string exe, string argsTail) SplitImagePath(string imagePath)
    {
        string s = imagePath.Trim();
        if (s.Length == 0) return ("", "");
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            return end > 0 ? (s.Substring(1, end - 1), s.Substring(end + 1)) : (s.Substring(1), "");
        }
        // Unquoted: prefer treating the whole string as the path if it resolves to a real file.
        if (File.Exists(Environment.ExpandEnvironmentVariables(s))) return (s, "");
        int sp = s.IndexOf(' ');
        return sp < 0 ? (s, "") : (s.Substring(0, sp), s.Substring(sp));
    }

    private static bool IsBuiltinAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account)) return true; // null/empty == LocalSystem
        string a = account.Trim();
        return a.Equals("LocalSystem", StringComparison.OrdinalIgnoreCase)
            || a.Equals(".\\LocalSystem", StringComparison.OrdinalIgnoreCase)
            || a.Equals("LocalService", StringComparison.OrdinalIgnoreCase)
            || a.Equals("NetworkService", StringComparison.OrdinalIgnoreCase)
            || a.Equals("NT AUTHORITY\\LocalService", StringComparison.OrdinalIgnoreCase)
            || a.Equals("NT AUTHORITY\\NetworkService", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFullPath(string p)
    {
        try { return Path.GetFullPath(p); } catch { return p; }
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(SafeFullPath(a), SafeFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string? ReadStr(char* p) => p == null ? null : new string(p);

    // Opens by service key name; if that name doesn't exist, treats it as a display name
    // and resolves the underlying key. Returns 0 only when the service genuinely isn't
    // installed. Access errors (etc.) throw.
    private static nint OpenServiceHandle(nint scm, string name, uint access)
    {
        nint svc = OpenServiceW(scm, name, access);
        if (svc != 0) return svc;

        int err = Marshal.GetLastPInvokeError();
        if (err != ERROR_SERVICE_DOES_NOT_EXIST)
            throw new InvalidOperationException("OpenService(\"" + name + "\") failed (error " + err + ").");

        // Maybe 'name' was a display name rather than the service key.
        string? key = TryResolveServiceKey(scm, name);
        if (key is null || string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            return 0; // not installed under either name

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
        // Service key names are capped at ~256 chars, so this buffer nearly always suffices.
        uint cch = 256;
        char* buf = stackalloc char[256];
        if (GetServiceKeyNameW(scm, displayName, buf, ref cch))
            return new string(buf, 0, (int)cch);

        if (Marshal.GetLastPInvokeError() != ERROR_INSUFFICIENT_BUFFER)
            return null; // display name not found (or other error) -> caller treats as not installed

        // Rare: name longer than the initial buffer. 'cch' now holds the required length.
        char* big = stackalloc char[(int)cch + 1];
        uint cap = cch + 1;
        if (GetServiceKeyNameW(scm, displayName, big, ref cap))
            return new string(big, 0, (int)cap);
        return null;
    }

    // Polls until the service reaches 'desired' or the timeout elapses. Honors the
    // service's own dwWaitHint (clamped) to avoid busy-waiting.
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

    // ---- Service config file ----------------------------------------------
    // Writes espmon.service.config.json into the install directory, pointing app_path at
    // the per-user Local AppData Espmon folder. Resolved against the *installer process's*
    // account -- which, under this project's on-demand elevation model, is the interactive
    // user. Same shape/indentation as the config the app ships.
    private static void WriteServiceConfig(string installRoot)
    {
        string appPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        string dest = Path.Combine(installRoot, ServiceConfigFileName);
        string json = "{\n    \"app_path\": \"" + JsonEscape(appPath) + "\"\n}";
        File.WriteAllText(dest, json);
    }

    // Minimal JSON string escaping -- enough for a Windows path (backslashes, quotes; other
    // control chars can't occur in a filesystem path).
    private static string JsonEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // ---- Reboot prompt ----------------------------------------------------
    // If the installer deleted the service this run, recommend a reboot and offer to do it.
    // A deleted service lingers as "marked for deletion" until reboot, and a recreated one
    // may not surface until then -- which is the behavior we're papering over here.
    private static void MaybePromptReboot(nint hWnd)
    {
        if (!_rebootNeeded) return;

        int r = MessageBoxW(hWnd,
            "It is recommended that the system be rebooted." +
            "Some functionality may not work until reboot.\n\nWould you like to reboot now?",
            AppName, MB_ICONINFORMATION | MB_YESNO);

        if (r == IDYES)
            RebootSystem(hWnd);
    }

    // Graceful reboot: apps receive the usual end-session messages and may block/delay it
    // (no EWX_FORCE). Requires SeShutdownPrivilege, which we enable on our own token first.
    private static void RebootSystem(nint hWnd)
    {
        if (!EnableShutdownPrivilege())
        {
            MessageBoxW(hWnd,
                "Couldn't obtain permission to reboot. Please reboot manually to finalize the installation.",
                AppName, MB_ICONWARNING);
            return;
        }

        if (!ExitWindowsEx(EWX_REBOOT, SHTDN_REASON_INSTALL))
            MessageBoxW(hWnd,
                "The reboot request failed (error " + Marshal.GetLastPInvokeError() +
                "). Please reboot manually to finalize the installation.",
                AppName, MB_ICONWARNING);
    }

    // Enables SeShutdownPrivilege on the current process token so ExitWindowsEx can run.
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

                // AdjustTokenPrivileges "succeeds" even when the privilege wasn't held; the
                // real verdict is in the last error (ERROR_NOT_ALL_ASSIGNED == 1300).
                return Marshal.GetLastPInvokeError() == 0;
            }
        }
        finally { CloseHandle(tok); }
    }
    // Wipes the target directory before extraction so stale files from a previous install
    // don't linger. The service is already stopped by this point, so its exe is unlocked.
    // If files are still locked (e.g. the main app is running), prompts the user to close it
    // and retry. Returns false if they cancel -- caller should abort the install.
    private static bool ClearInstallDir(nint hWnd, string installRoot)
    {
        string root = Path.GetFullPath(installRoot);
        if (!Directory.Exists(root)) return true; // nothing to clear -> proceed

        // Guard: never wipe a drive root (e.g. "C:\") -- a typo in the install box shouldn't
        // nuke a whole volume. Remove this block if you don't want the check.
        string? drive = Path.GetPathRoot(root);
        if (drive is not null &&
            drive.TrimEnd('\\', '/').Equals(root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clear a drive root: " + root);

        while (true)
        {
            try
            {
                // Clear read-only/hidden attributes first (Directory.Delete throws on them).
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best effort */ }
                }
                Directory.Delete(root, recursive: true);
                return true; // cleared -> proceed (ExtractEmbeddedZip recreates the dir)
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                int r = MessageBoxW(hWnd,
                    "Some files in the install folder are in use. Please close any running instance of " +
                    AppName + " and try again.",
                    AppName, MB_ICONWARNING | MB_RETRYCANCEL);
                if (r == IDRETRY) continue; // user closed the app -> try the delete again
                _rebootNeeded = true;
                return false;
            }
        }
    }
    // ---- Extraction (AOT-safe) --------------------------------------------
    private static void ExtractEmbeddedZip(string installRoot)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? resource = null;
        foreach (string name in asm.GetManifestResourceNames())
            if (name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase)) { resource = name; break; }
        if (resource is null)
            throw new InvalidOperationException("Embedded resource ending in '" + ResourceSuffix + "' not found.");

        using Stream? s = asm.GetManifestResourceStream(resource);
        using var zip = new ZipArchive(s!, ZipArchiveMode.Read);

        string root = Path.GetFullPath(installRoot);
        string rootSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        foreach (ZipArchiveEntry e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue;

            // The archive carries its own root folder ("Espmon/..."). Drop that first
            // segment so the remaining tree is re-rooted directly at installRoot. This
            // is what lets the user rename the install folder (the last segment of the
            // path they chose) without changing the zip itself.
            string entryPath = e.FullName.Replace('\\', '/');
            int slash = entryPath.IndexOf('/');
            string relative = slash >= 0 ? entryPath.Substring(slash + 1) : entryPath;
            if (relative.Length == 0) continue;

            string dest = Path.GetFullPath(Path.Combine(root, relative));
            if (!dest.StartsWith(rootSep, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Zip entry escapes target: " + e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, overwrite: true);
        }
    }

    // ---- Shortcut via source-generated COM (AOT-safe) ----------------------
    private static void CreateShortcut(string lnkPath, string targetExe)
    {
        Guid clsid = CLSID_ShellLink;
        Guid iid = IID_IShellLinkW;
        int hr = CoCreateInstance(ref clsid, 0, CLSCTX_INPROC_SERVER, ref iid, out nint punk);
        if (hr < 0) throw new COMException("CoCreateInstance(ShellLink) failed", hr);
        try
        {
            object rcw = Com.GetOrCreateObjectForComInstance(punk, CreateObjectFlags.None);
            var link = (IShellLinkW)rcw;
            link.SetPath(targetExe);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetExe) ?? "");
            link.SetDescription(AppName);
            ((IPersistFile)rcw).Save(lnkPath, true);
        }
        finally
        {
            Marshal.Release(punk);
        }
    }

    // ---- small helpers -----------------------------------------------------
    private static bool IsChecked(nint h) => (int)SendMessageW(h, BM_GETCHECK, 0, 0) == BST_CHECKED;

    private static string GetText(nint h)
    {
        int len = GetWindowTextLengthW(h);
        if (len <= 0) return "";
        char* buf = stackalloc char[len + 1];
        int n = GetWindowTextW(h, buf, len + 1);
        return new string(buf, 0, n);
    }

    private static string LeafOf(string path)
    {
        string leaf = Path.GetFileName(path.TrimEnd('\\', '/', ' '));
        return string.IsNullOrEmpty(leaf) ? AppName : leaf;
    }

    // ====================== constants =======================================
    private const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_CLOSE = 0x0010,
                       WM_COMMAND = 0x0111, WM_SETFONT = 0x0030, BM_GETCHECK = 0x00F0;
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_THICKFRAME = 0x00040000,
                       WS_MAXIMIZEBOX = 0x00010000, WS_CHILD = 0x40000000,
                       WS_VISIBLE = 0x10000000, WS_BORDER = 0x00800000, WS_TABSTOP = 0x00010000;
    private const uint ES_AUTOHSCROLL = 0x0080;
    private const uint BS_PUSHBUTTON = 0x0000, BS_DEFPUSHBUTTON = 0x0001, BS_AUTOCHECKBOX = 0x0003;
    private const uint SS_LEFT = 0x0000;
    private const uint BIF_RETURNONLYFSDIRS = 0x0001, BIF_NEWDIALOGSTYLE = 0x0040;
    private const uint MB_ICONERROR = 0x10, MB_ICONWARNING = 0x30, MB_ICONINFORMATION = 0x40;
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_RETRYCANCEL = 0x00000005;
    private const int IDRETRY = 4;
    private const int IDYES = 6;
    private const int SW_SHOW = 5, COLOR_BTNFACE = 15, IDC_ARROW = 32512,
                      DEFAULT_GUI_FONT = 17, BST_CHECKED = 1, CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private static readonly int CW_USEDEFAULT = unchecked((int)0x80000000);

    // Service Control Manager
    private const uint SC_MANAGER_CONNECT = 0x0001, SC_MANAGER_CREATE_SERVICE = 0x0002;
    private const uint SERVICE_QUERY_CONFIG = 0x0001, SERVICE_QUERY_STATUS = 0x0004,
                       SERVICE_START = 0x0010, SERVICE_STOP = 0x0020;
    private const uint DELETE = 0x00010000, SERVICE_ALL_ACCESS = 0x000F01FF;
    private const uint SERVICE_CONTROL_STOP = 0x00000001;
    private const uint SERVICE_CONFIG_DESCRIPTION = 1;
    private const uint SERVICE_STOPPED = 0x1, SERVICE_STOP_PENDING = 0x3, SERVICE_RUNNING = 0x4;
    private const int ERROR_INSUFFICIENT_BUFFER = 122, ERROR_SERVICE_ALREADY_RUNNING = 1056,
                      ERROR_SERVICE_DOES_NOT_EXIST = 1060, ERROR_SERVICE_NOT_ACTIVE = 1062,
                      ERROR_SERVICE_MARKED_FOR_DELETE = 1072;

    // Reboot / shutdown privilege
    private const uint EWX_REBOOT = 0x00000002;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020, TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    // MAJOR_APPLICATION | MINOR_INSTALLATION | FLAG_PLANNED -- a clean, expected reboot.
    private const uint SHTDN_REASON_INSTALL = 0x00040000 | 0x00000002 | 0x80000000;

    private static readonly Guid CLSID_ShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IID_IShellLinkW = new("000214F9-0000-0000-C000-000000000046");

    // ====================== P/Invoke ========================================
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("user32.dll")]
    private static partial ushort RegisterClassExW(WNDCLASSEXW* lpwcx);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string windowName,
        uint style, int x, int y, int w, int h, nint parent, nint menu, nint hInstance, void* param);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint h, uint msg, nint w, nint l);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint h, int cmd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint h);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(MSG* msg, nint h, uint min, uint max);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(MSG* msg);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(MSG* msg);

    [LibraryImport("user32.dll")]
    private static partial void PostQuitMessage(int code);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint h);

    [LibraryImport("user32.dll")]
    private static partial nint LoadCursorW(nint hInst, char* name);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint h, uint msg, nint w, nint l);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowTextW(nint h, string s);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(nint h, char* s, int max);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint h);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint h, string text, string caption, uint type);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ExitWindowsEx(uint uFlags, uint dwReason);

    [LibraryImport("gdi32.dll")]
    private static partial nint GetStockObject(int i);

    [LibraryImport("shell32.dll")]
    private static partial nint SHBrowseForFolderW(BROWSEINFOW* lpbi);

    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SHGetPathFromIDListW(nint pidl, char* pszPath);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint pv);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(ref Guid rclsid, nint outer, uint ctx,
                                                ref Guid riid, out nint ppv);

    [LibraryImport("kernel32.dll")]
    private static partial void Sleep(uint dwMilliseconds);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

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

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool StartServiceW(nint hService, uint numArgs, char** args);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint hSCObject);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfigW(nint hService, QUERY_SERVICE_CONFIGW* cfg,
                                                    uint cbBufSize, uint* pcbBytesNeeded);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteService(nint hService);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ChangeServiceConfig2W(nint hService, uint dwInfoLevel, void* lpInfo);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateServiceW(
        nint hSCManager, string lpServiceName, string? lpDisplayName, uint dwDesiredAccess,
        uint dwServiceType, uint dwStartType, uint dwErrorControl, string lpBinaryPathName,
        char* lpLoadOrderGroup, uint* lpdwTagId, char* lpDependencies,
        string? lpServiceStartName, string? lpPassword);

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
    private struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public nint lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

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
    private struct SERVICE_DESCRIPTION
    {
        public char* lpDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    // TOKEN_PRIVILEGES with a single privilege entry flattened inline (PrivilegeCount is
    // always 1 here), so the LUID_AND_ATTRIBUTES fields sit directly after the count.
    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QUERY_SERVICE_CONFIGW
    {
        public uint dwServiceType;
        public uint dwStartType;
        public uint dwErrorControl;
        public char* lpBinaryPathName;
        public char* lpLoadOrderGroup;
        public uint dwTagId;
        public char* lpDependencies;
        public char* lpServiceStartName;
        public char* lpDisplayName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BROWSEINFOW
    {
        public nint hwndOwner, pidlRoot;
        public char* pszDisplayName;
        public char* lpszTitle;
        public uint ulFlags;
        public nint lpfn, lParam;
        public int iImage;
    }
}

// ====================== source-generated COM ===============================
[GeneratedComInterface]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal partial interface IShellLinkW
{
    [PreserveSig] int _0();
    [PreserveSig] int _1();
    [PreserveSig] int _2();
    [PreserveSig] int _3();
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);   // slot 4
    [PreserveSig] int _5();
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir); // slot 6
    [PreserveSig] int _7();
    [PreserveSig] int _8();
    [PreserveSig] int _9();
    [PreserveSig] int _10();
    [PreserveSig] int _11();
    [PreserveSig] int _12();
    [PreserveSig] int _13();
    [PreserveSig] int _14();
    [PreserveSig] int _15();
    [PreserveSig] int _16();
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);          // slot 17
}

[GeneratedComInterface]
[Guid("0000010b-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    [PreserveSig] int _p0();
    [PreserveSig] int _p1();
    [PreserveSig] int _p2();
    void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName,
              [MarshalAs(UnmanagedType.Bool)] bool remember);                // slot 3
}