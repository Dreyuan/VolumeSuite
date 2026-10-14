using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VolumeSuite.Services;

/// <summary>
/// 用未公开 API SetWindowCompositionAttribute 给窗口开模糊效果。
/// 注意：不会修改 CompositionTarget.BackgroundColor，那个由窗口自己一次性设置。
/// </summary>
public static class AcrylicHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    private enum AccentState
    {
        Disabled = 0,
        EnableGradient = 1,
        EnableTransparentGradient = 2,
        EnableBlurBehind = 3,
        EnableAcrylicBlurBehind = 4,
        EnableHostBackdrop = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute
    {
        WcaAccentPolicy = 19,
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttributeData data);

    public static void EnableAcrylic(Window window, uint tintColor = 0x99000000)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // ★ 关键：告诉 WPF 合成器不画不透明背景，DWM 的模糊才能透出来
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget != null)
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;

        System.Diagnostics.Debug.WriteLine(
    $"[Acrylic] CompositionTarget 设为 {source?.CompositionTarget?.BackgroundColor}");

        var accent = new AccentPolicy
        {
            AccentState = AccentState.EnableBlurBehind,
            AccentFlags = 0,
            GradientColor = 0x00000000,
        };
        Apply(hwnd, accent);
    }

    public static void DisableAcrylic(Window window)
    {
        // 空实现：DWM 一直处于 BlurBehind 状态。
        // 图片模式的"不透明感"由 OpaqueUnderlay 提供，不需要真关闭 Acrylic。
    }

    private static void Apply(IntPtr hwnd, AccentPolicy accent)
    {
        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WcaAccentPolicy,
                Data = ptr,
                SizeOfData = size,
            };
            int result = SetWindowCompositionAttribute(hwnd, ref data);
            System.Diagnostics.Debug.WriteLine(
                $"[Acrylic] 调用结果={result} State={accent.AccentState}");
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}