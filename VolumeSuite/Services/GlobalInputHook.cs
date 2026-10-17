using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace VolumeSuite.Services;

public static class GlobalInputHook
{
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;

    private static IntPtr _mouseHookID = IntPtr.Zero;
    private static IntPtr _keyboardHookID = IntPtr.Zero;
    private static LowLevelMouseProc _mouseProc = MouseHookCallback;
    private static LowLevelKeyboardProc _keyboardProc = KeyboardHookCallback;

    public static event Action<int, int>? MouseMoved;
    public static event Action? MouseLeftClicked;
    public static event Action? MouseRightClicked;
    public static event Action? EnterPressed;
    public static event Action? EscapePressed;

    public static void Start()
    {
        if (_mouseHookID == IntPtr.Zero)
            _mouseHookID = SetHook(WH_MOUSE_LL, _mouseProc);
        if (_keyboardHookID == IntPtr.Zero)
            _keyboardHookID = SetHook(WH_KEYBOARD_LL, _keyboardProc);
    }

    public static void Stop()
    {
        if (_mouseHookID != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHookID); _mouseHookID = IntPtr.Zero; }
        if (_keyboardHookID != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHookID); _keyboardHookID = IntPtr.Zero; }
    }

    private static IntPtr SetHook(int hookType, Delegate proc)
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_MOUSEMOVE)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                MouseMoved?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
            }
            else if (msg == WM_LBUTTONDOWN) MouseLeftClicked?.Invoke();
            else if (msg == WM_RBUTTONDOWN) MouseRightClicked?.Invoke();
        }
        return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
    }

    private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VK_RETURN) EnterPressed?.Invoke();
            else if (vkCode == VK_ESCAPE) EscapePressed?.Invoke();
        }
        return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}