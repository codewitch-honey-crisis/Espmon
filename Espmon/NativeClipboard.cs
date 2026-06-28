using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Espmon
{
    internal static class Win32Clipboard
    {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr hWndNewOwner);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint uFormat);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalFree(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr hMem);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalUnlock(IntPtr hMem);

        public static bool HasText() => IsClipboardFormatAvailable(CF_UNICODETEXT);

        // OpenClipboard(NULL) ties the clipboard to the current task — no HWND needed.
        // Retry because another app may briefly hold it open.
        private static bool TryOpen(int retries = 10)
        {
            for (int i = 0; i < retries; i++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }

        public static bool SetText(string text)
        {
            if (text == null) return false;
            if (!TryOpen()) return false;
            try
            {
                EmptyClipboard();

                int bytes = (text.Length + 1) * 2;                       // UTF-16 + null terminator
                IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                if (hGlobal == IntPtr.Zero) return false;

                IntPtr target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero) { GlobalFree(hGlobal); return false; }
                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target, text.Length * 2, 0);      // null terminator
                }
                finally { GlobalUnlock(hGlobal); }

                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    Debug.WriteLine($"[win32] SetClipboardData failed, err={Marshal.GetLastWin32Error()}");
                    GlobalFree(hGlobal);
                    return false;
                }
                Debug.WriteLine("[win32] SetClipboardData OK");
                return true;                                             // on success the system owns hGlobal
            }
            finally { CloseClipboard(); }
        }

        public static string? GetText()
        {
            if (!HasText()) return null;
            if (!TryOpen()) return null;
            try
            {
                IntPtr handle = GetClipboardData(CF_UNICODETEXT);
                if (handle == IntPtr.Zero) return null;
                IntPtr ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) return null;
                try { return Marshal.PtrToStringUni(ptr); }
                finally { GlobalUnlock(handle); }
            }
            finally { CloseClipboard(); }
        }
    }
}
