using System;
using System.Runtime.InteropServices;

namespace Syncro.CLI;

public static class Win32Api
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        IntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public static void BroadcastSettingsChange()
    {
        // Broadcast WM_SETTINGCHANGE to notify Windows Explorer/CMD/other apps that environment variables changed
        _ = SendMessageTimeout(
            new IntPtr(0xffff), // HWND_BROADCAST
            WM_SETTINGCHANGE,
            IntPtr.Zero,
            "Environment",
            SMTO_ABORTIFHUNG,
            5000,
            out _);
    }
}
