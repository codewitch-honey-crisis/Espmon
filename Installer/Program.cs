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
    // -----------------------------------------------------------------------

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
        try
        {
            string target = GetText(_hEdit);
            if (string.IsNullOrWhiteSpace(target))
            {
                MessageBoxW(hWnd, "Please choose an install location.", AppName, MB_ICONWARNING);
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

            MessageBoxW(hWnd, AppName + " was installed to:\n" + target, AppName, MB_ICONINFORMATION);
            DestroyWindow(hWnd); // success: tear down -> WM_DESTROY -> PostQuitMessage -> exit
        }
        catch (Exception ex)
        {
            MessageBoxW(hWnd, "Install failed:\n" + ex.Message, AppName, MB_ICONERROR);
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
    private const int SW_SHOW = 5, COLOR_BTNFACE = 15, IDC_ARROW = 32512,
                      DEFAULT_GUI_FONT = 17, BST_CHECKED = 1, CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private static readonly int CW_USEDEFAULT = unchecked((int)0x80000000);

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