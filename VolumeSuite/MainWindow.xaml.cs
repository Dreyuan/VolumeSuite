using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using VolumeSuite.Services;
using VolumeSuite.ViewModels;
using static VolumeSuite.ViewModels.DropdownItemViewModel;

namespace VolumeSuite;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private HotkeyItemViewModel? _capturingHotkey;
    private readonly List<string> _captureKeys = new();
    private System.Windows.Threading.DispatcherTimer? _windowSaveTimer;
    private bool _isRestoringWindow = false; // 防止恢复时误触发保存

    public MainWindow()
    {
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        // 窗口大小改变时重新计算圆角
        SizeChanged += (_, _) => ApplyWindowCorner();

        // AnySettingsChanged 里
        ApplyWindowCorner(_lastStraightCorner);

        // 监听窗口位置、大小、状态变化
        LocationChanged += (_, _) => TriggerSaveWindowPlacement();
        SizeChanged += (_, _) => TriggerSaveWindowPlacement();
        StateChanged += (_, _) => TriggerSaveWindowPlacement();

        PreviewKeyDown += MainWindow_PreviewKeyDown;
        InitColorPickerHooks();  // ★ 初始化吸色钩子
        InitCornerHooks();



        ViewModel.LauncherSettings.SettingsChanged += ApplyBackground;

        // 启动器设置里任意一项变化时，重新应用背景
        SettingsItemViewModel.AnySettingsChanged += (key) =>
        {
            if (ViewModel?.SelectedTool != ViewModel?.LauncherSettings) return;

            // ★ 如果改的是 8 位 HEX 色调，同步 alpha 到滑条
            SyncTintOpacityFromHex(key);

            ApplyBackground();

            // ★ 加上这一行
            ApplyWindowCorner();
        };

        // 监听选中的工具变化，触发动画
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTool))
                AnimateToolChange();
        };

        Loaded += (_, _) =>
        {
            var source = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor =
                    System.Windows.Media.Colors.Transparent;

            ApplyBackground();
            ApplyWindowCorner();
            RestoreWindowPlacement();

            VolumeSuite.Services.GlobalInputHook.Start(); // ★ 启动全局钩子
        };
    }

    // ============ 初始化全局吸色钩子 ============

    private int _lastHookX;
    private int _lastHookY;

    private void InitColorPickerHooks()
    {
        VolumeSuite.Services.GlobalInputHook.MouseMoved += (x, y) =>
        {
            _lastHookX = x;
            _lastHookY = y;
            var vm = FindPickingColorItem();
            if (vm == null) return;
            var color = GetScreenColor(x, y);
            vm.UpdatePick(color);
        };

        VolumeSuite.Services.GlobalInputHook.EnterPressed += () =>
        {
            var vm = FindPickingColorItem();
            if (vm == null) return;
            var color = GetScreenColor(_lastHookX, _lastHookY);
            Dispatcher.BeginInvoke(new Action(() => vm.CommitPick(color)));
        };

        VolumeSuite.Services.GlobalInputHook.MouseRightClicked += () =>
        {
            var vm = FindPickingColorItem();
            if (vm == null) return;
            Dispatcher.BeginInvoke(new Action(() => vm.CancelPick()));
        };

        VolumeSuite.Services.GlobalInputHook.EscapePressed += () =>
        {
            var vm = FindPickingColorItem();
            if (vm == null) return;
            Dispatcher.BeginInvoke(new Action(() => vm.CancelPick()));
        };
    }

    // 辅助方法：遍历当前设置，找出正在吸色的那一个
    private ColorItemViewModel? FindPickingColorItem()
    {
        if (ViewModel?.SelectedTool?.Sections == null) return null;
        foreach (var sec in ViewModel.SelectedTool.Sections)
        {
            foreach (var item in sec.Items)
            {
                if (item is ColorItemViewModel cvm && cvm.IsPicking)
                    return cvm;
            }
        }
        return null;
    }

    // 获取屏幕像素颜色（Win32 API）

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

    [DllImport("gdi32.dll")]
    static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int iCombineMode);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr hObject);

    private const int RGN_OR = 2;
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

    [DllImport("user32.dll")]

    static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

    private static System.Windows.Media.Color GetScreenColor(int x, int y)
    {
        IntPtr hdc = GetDC(IntPtr.Zero);
        uint pixel = GetPixel(hdc, x, y);
        ReleaseDC(IntPtr.Zero, hdc);

        byte r = (byte)(pixel & 0x000000FF);
        byte g = (byte)((pixel & 0x0000FF00) >> 8);
        byte b = (byte)((pixel & 0x00FF0000) >> 16);
        return System.Windows.Media.Color.FromRgb(r, g, b);
    }

    /// <summary>点 X 时只隐藏窗口，不真的关闭。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        VolumeSuite.Services.GlobalInputHook.Stop();
        SaveWindowPlacement(); // ★ 退出前保存一次
        e.Cancel = true;
        Hide();
    }

    // ============ 侧栏 ============

    private void SideItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ToolViewModel vm)
            ViewModel.SelectedTool = vm;
    }

    private void SideItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ToolViewModel vm)
            return;

        var menu = new ContextMenu();

        var openDirItem = new MenuItem { Header = "打开配置目录" };
        openDirItem.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", vm.Manifest.Directory);
            }
            catch { }
        };
        menu.Items.Add(openDirItem);

        var restartItem = new MenuItem { Header = "重启工具" };
        restartItem.Click += (_, _) =>
        {
            vm.Stop();
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            timer.Tick += (s, _) =>
            {
                timer.Stop();
                vm.Start();
            };
            timer.Start();
        };
        menu.Items.Add(restartItem);

        menu.Items.Add(new Separator());

        var moreItem = new MenuItem { Header = "更多设置..." };
        moreItem.Click += (_, _) =>
        {
            System.Windows.MessageBox.Show(
                "复杂设置（如截图区域预览）由工具自己弹窗，暂未实现。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        menu.Items.Add(moreItem);

        menu.PlacementTarget = fe;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>左下角"启动器设置"按钮</summary>
    private void LauncherSettings_Click(object sender, MouseButtonEventArgs e)
    {
        ViewModel.SelectedTool = ViewModel.LauncherSettings;
    }

    // ============ 启停按钮 ============

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ToolViewModel vm)
            vm.Toggle();
    }

    // ============ 热键捕获 ============

    private void HotkeyBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border box || box.DataContext is not HotkeyItemViewModel hk)
            return;

        _capturingHotkey = hk;
        _captureKeys.Clear();
        hk.Display = "按下快捷键...";
        e.Handled = true;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingHotkey == null) return;

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _capturingHotkey.CancelCapture();
            _capturingHotkey = null;
            _captureKeys.Clear();
            return;
        }

        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin)
        {
            string name = KeyToName(key);
            if (!_captureKeys.Contains(name))
                _captureKeys.Add(name);

            _capturingHotkey.Display = string.Join(" + ", _captureKeys) + " + ...";
            return;
        }

        string final = KeyToName(key);
        _captureKeys.Add(final);

        _capturingHotkey.UpdateKeys(new List<string>(_captureKeys));
        _capturingHotkey = null;
        _captureKeys.Clear();
    }

    private void InitCornerHooks()
    {
        VolumeSuite.Services.GlobalInputHook.MouseMoved += (x, y) =>
        {
            if (FindPickingColorItem() != null || WindowState != WindowState.Normal) return;

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            var topLeft = PointToScreen(new Point(0, 0));
            var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));

            int hitZone = 20;

            bool nearLeft = Math.Abs(x - topLeft.X) < hitZone;
            bool nearRight = Math.Abs(x - bottomRight.X) < hitZone;
            bool nearTop = Math.Abs(y - topLeft.Y) < hitZone;
            bool nearBottom = Math.Abs(y - bottomRight.Y) < hitZone;

            string? straightCorner = null;
            if (nearLeft && nearTop) straightCorner = "TopLeft";
            else if (nearRight && nearTop) straightCorner = "TopRight";
            else if (nearLeft && nearBottom) straightCorner = "BottomLeft";
            else if (nearRight && nearBottom) straightCorner = "BottomRight";

            // 只有状态改变时才重绘圆角，避免频繁触发导致卡顿
            if (straightCorner != _lastStraightCorner)
            {
                _lastStraightCorner = straightCorner;
                Dispatcher.BeginInvoke(new Action(() => ApplyWindowCorner(_lastStraightCorner)));
            }
        };
    }
    private string? _lastStraightCorner = null;

    

    private static string KeyToName(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "ControlKey",
        Key.LeftShift or Key.RightShift => "ShiftKey",
        Key.LeftAlt or Key.RightAlt => "Menu",
        Key.LWin => "LWin",
        Key.RWin => "RWin",
        _ => key.ToString()
    };

    // ============ path 类型：浏览按钮 ============

    private void PathBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not PathItemViewModel pvm)
            return;

        if (pvm.Mode == "folder")
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                InitialDirectory = Directory.Exists(pvm.Value) ? pvm.Value : "",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
                pvm.Value = dialog.FolderName;
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                FileName = File.Exists(pvm.Value)
                    ? Path.GetFileName(pvm.Value) : "",
                InitialDirectory = File.Exists(pvm.Value)
                    ? Path.GetDirectoryName(pvm.Value) : "",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*"
            };
            if (dialog.ShowDialog() == true)
                pvm.Value = dialog.FileName;
        }
    }

    // ============ 颜色项：展开/收起 ============

    private void ColorExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ColorItemViewModel vm)
            vm.IsExpanded = !vm.IsExpanded;
    }

    // ============ 颜色选择器：常用色板 / 收藏夹 ============

    private void CommonColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ColorSlot slot && slot.Parent != null)
            slot.Parent.ApplyHex(slot.Hex);
    }

    private void SavedColor_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ColorSlot slot && slot.Parent != null)
        {
            if (slot.IsEmpty)
                slot.Parent.SaveCurrentToSlot(slot.Index);
            else
                slot.Parent.ApplyHex(slot.Hex);
        }
    }

    private void SavedColor_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ColorSlot slot && slot.Parent != null)
        {
            if (!slot.IsEmpty)
            {
                slot.Parent.ClearSlot(slot.Index);
                e.Handled = true;
            }
        }
    }

    // ============ 滑条拖动：开始/结束 ============

    private void NumberSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s && s.DataContext is NumberItemViewModel vm)
            vm.BeginDrag();
    }

    private void NumberSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider s && s.DataContext is NumberItemViewModel vm)
            vm.EndDrag();
    }

    // ============ 背景：根据启动器设置应用 ============

    private void ApplyBackground()
    {
        if (ViewModel.LauncherSettings == null) return;

        var store = ViewModel.LauncherSettings.Store;
        var mode = store.GetString("BackgroundMode", "image");
        var path = store.GetString("BackgroundImagePath", "");

        // ===== 标题栏色调 =====
        var titleBarMode = store.GetString("TitleBarTint", "follow");
        System.Windows.Media.Brush titleBarBrush = titleBarMode switch
        {
            "half" => new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromArgb(0x66, 0, 0, 0)),
            "solid" => new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A)),
            _ => System.Windows.Media.Brushes.Transparent,   // "follow"
        };
        if (titleBarBrush.CanFreeze) titleBarBrush.Freeze();

        if (mode == "acrylic")
        {
            // ===== 毛玻璃 =====
            if (OpaqueUnderlay != null)
                OpaqueUnderlay.Visibility = Visibility.Collapsed;

            if (BackgroundImage != null)
                BackgroundImage.Visibility = Visibility.Collapsed;

            // 读毛玻璃色调
            var tintHex = store.GetString("AcrylicTintColor", "#000000");
            var tintOpacity = store.GetDouble("AcrylicTintOpacity", 20);
            if (tintOpacity < 0) tintOpacity = 20;

            var baseColor = ParseHexColor(tintHex, System.Windows.Media.Colors.Black);
            byte alpha = (byte)Math.Clamp(tintOpacity * 255.0 / 100.0, 0, 255);
            var tintBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            tintBrush.Freeze();

            if (BackgroundShade != null)
            {
                BackgroundShade.Visibility = Visibility.Visible;
                BackgroundShade.Background = tintBrush;
            }

            if (AppTitleBar != null)
                AppTitleBar.Background = titleBarBrush;

            AcrylicHelper.EnableAcrylic(this, 0x00000000);
            return;
        }

        // ===== 图片模式 =====
        AcrylicHelper.DisableAcrylic(this);

        if (OpaqueUnderlay != null)
            OpaqueUnderlay.Visibility = Visibility.Visible;

        if (BackgroundImage != null)
        {
            BackgroundImage.Visibility = Visibility.Visible;

            // 图片不透明度
            var imgOpacity = store.GetDouble("ImageOpacity", 40);
            if (imgOpacity < 0) imgOpacity = 40;
            BackgroundImage.Opacity = Math.Clamp(imgOpacity / 100.0, 0, 1);

            // 图片模糊
            var blurRadius = store.GetDouble("ImageBlurRadius", 0);
            if (blurRadius <= 0)
                BackgroundImage.Effect = null;
            else
                BackgroundImage.Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = blurRadius,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                };
        }

        if (BackgroundShade != null)
        {
            var shadeDepth = store.GetDouble("ImageShadeDepth", 60);
            if (shadeDepth < 0) shadeDepth = 60;
            byte shadeAlpha = (byte)Math.Clamp(shadeDepth * 255.0 / 100.0, 0, 255);
            BackgroundShade.Visibility = Visibility.Visible;
            BackgroundShade.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(shadeAlpha, 0, 0, 0));
        }

        if (AppTitleBar != null)
            AppTitleBar.Background = titleBarBrush;

        BitmapImage? bmp = null;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            bmp = LoadBitmapFromFile(path);
        if (bmp == null)
            bmp = LoadBitmapFromResource("pack://application:,,,/background.png");
        if (BackgroundImage != null && bmp != null)
            BackgroundImage.Source = bmp;
    }

    /// <summary>
    /// 解析 HEX 颜色，支持 #RRGGBB、#AARRGGBB、RRGGBB（不带 #）。
    /// 解析失败返回 fallback。
    /// </summary>
    private static System.Windows.Media.Color ParseHexColor(
        string hex, System.Windows.Media.Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        hex = hex.Trim().TrimStart('#');
        try
        {
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return System.Windows.Media.Color.FromRgb(r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return System.Windows.Media.Color.FromArgb(a, r, g, b);
            }
        }
        catch { }
        return fallback;
    }

    /// <summary>
    /// 加载图片，兼容带 EXIF/ICC 元数据的"问题图片"。
    /// </summary>
    private static BitmapImage? LoadBitmapFromFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            bmp.StreamSource = stream;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { }

        try
        {
            using var src = System.Drawing.Image.FromFile(path);
            using var ms = new MemoryStream();
            src.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"图片加载失败 [{path}]：{ex.Message}");
            return null;
        }
    }

    /// <summary>从 pack:// 资源路径加载 BitmapImage</summary>
    private static BitmapImage? LoadBitmapFromResource(string uri)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(uri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    // ============ 任务栏缩略图：强制显示图标 ============

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_FORCE_ICONIC_REPRESENTATION = 7;

    private void SetIconicThumbnail(bool iconic)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int value = iconic ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_FORCE_ICONIC_REPRESENTATION,
                ref value, sizeof(int));
        }
        catch { }
    }

    // ============ 所有设置项的输入框：回车 = 立即提交 ============

    private void SettingTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is not TextBox tb) return;

        tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Keyboard.ClearFocus();

        e.Handled = true;
    }

    // ============ 8 位 HEX 色调 → 同步滑条 alpha ============

    private bool _syncingTint;

    private void SyncTintOpacityFromHex(string changedKey)
    {
        if (_syncingTint) return;
        if (changedKey != "AcrylicTintColor") return;

        var ls = ViewModel?.LauncherSettings;
        if (ls == null) return;

        var hex = ls.Store.GetString("AcrylicTintColor", "");
        var clean = hex.Trim().TrimStart('#');
        if (clean.Length != 8) return;   // 只有 8 位才同步

        byte alpha;
        try { alpha = Convert.ToByte(clean.Substring(0, 2), 16); }
        catch { return; }

        double opacity = Math.Round(alpha * 100.0 / 255.0);

        // 找到 AcrylicTintOpacity 的 VM，把 alpha 写进去
        _syncingTint = true;
        try
        {
            foreach (var section in ls.Sections)
                foreach (var item in section.Items)
                    if (item is NumberItemViewModel nvm && nvm.Key == "AcrylicTintOpacity")
                        nvm.Value = opacity;
        }
        finally
        {
            _syncingTint = false;
        }
    }
    private void StartPickColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ColorItemViewModel vm)
            vm.StartPick();
    }
    // ============ 应用窗口圆角 ============

    // ============ 应用窗口圆角（支持单角变直） ============

    private void ApplyWindowCorner(string? straightCorner = null)
    {
        var ls = ViewModel?.LauncherSettings;
        if (ls == null) return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (WindowState == WindowState.Maximized)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        double radius = ls.Store.GetDouble("WindowCornerRadius", 12);
        if (radius <= 0)
        {
            SetWindowRgn(hwnd, IntPtr.Zero, true);
            return;
        }

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        int scaledRadius = (int)(radius * dpi.DpiScaleX);
        int r2 = scaledRadius * 2;

        // ★ 修复了 DPI 缩放下的尺寸计算问题
        int width = (int)(ActualWidth * dpi.DpiScaleX);
        int height = (int)(ActualHeight * dpi.DpiScaleY);
        if (width <= 0 || height <= 0) return;

        // 1. 先画一个正常的圆角区域
        IntPtr hRgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, r2, r2);

        // 2. 如果指定了需要变直角的角，就用矩形把它填平
        if (!string.IsNullOrEmpty(straightCorner))
        {
            IntPtr hCorner = IntPtr.Zero;
            if (straightCorner == "TopLeft")
                hCorner = CreateRectRgn(0, 0, r2, r2);
            else if (straightCorner == "TopRight")
                hCorner = CreateRectRgn(width - r2, 0, width + 1, r2);
            else if (straightCorner == "BottomLeft")
                hCorner = CreateRectRgn(0, height - r2, r2, height + 1);
            else if (straightCorner == "BottomRight")
                hCorner = CreateRectRgn(width - r2, height - r2, width + 1, height + 1);

            if (hCorner != IntPtr.Zero)
            {
                CombineRgn(hRgn, hRgn, hCorner, RGN_OR); // 合并区域
                DeleteObject(hCorner); // 释放临时矩形内存
            }
        }

        SetWindowRgn(hwnd, hRgn, true);
    }
    // ============ 窗口位置/大小记忆 ============

    private void RestoreWindowPlacement()
    {
        var ls = ViewModel?.LauncherSettings;
        if (ls == null) return;

        _isRestoringWindow = true;
        try
        {
            double w = ls.Store.GetDouble("WindowWidth", 0);
            double h = ls.Store.GetDouble("WindowHeight", 0);
            double x = ls.Store.GetDouble("WindowLeft", -99999);
            double y = ls.Store.GetDouble("WindowTop", -99999);
            bool isMax = ls.Store.GetBool("WindowMaximized", false);

            if (w >= MinWidth && h >= MinHeight)
            {
                Width = w;
                Height = h;
            }

            // 检查位置是否在当前所有屏幕的范围内（防拔掉副屏导致窗口飞走）
            bool isPositionValid = false;
            if (x != -99999 && y != -99999)
            {
                var virtualScreen = new Rect(
                    SystemParameters.VirtualScreenLeft,
                    SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth,
                    SystemParameters.VirtualScreenHeight);

                // 只要窗口的标题栏有一半在屏幕内，就认为合法
                if (virtualScreen.Contains(x + (Width / 2), y + 15))
                {
                    isPositionValid = true;
                }
            }

            if (isPositionValid)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = x;
                Top = y;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            if (isMax) WindowState = WindowState.Maximized;
        }
        finally
        {
            _isRestoringWindow = false;
        }
    }

    private void TriggerSaveWindowPlacement()
    {
        if (_isRestoringWindow || ViewModel?.LauncherSettings == null) return;
        if (WindowState == WindowState.Minimized) return; // 最小化不记位置

        _windowSaveTimer?.Stop();
        _windowSaveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _windowSaveTimer.Tick += (_, _) =>
        {
            _windowSaveTimer.Stop();
            SaveWindowPlacement();
        };
        _windowSaveTimer.Start();
    }

    private void SaveWindowPlacement()
    {
        var ls = ViewModel?.LauncherSettings;
        if (ls == null) return;

        // 记录是否最大化
        bool isMax = WindowState == WindowState.Maximized;
        ls.Store.SetBool("WindowMaximized", isMax);

        // 如果当前是 Normal 状态，才记录真实的 Left/Top/Width/Height
        // 如果是 Maximized，RestoreBounds 会给出它恢复成普通窗口时的尺寸和位置
        if (WindowState == WindowState.Normal)
        {
            ls.Store.SetDouble("WindowWidth", ActualWidth);
            ls.Store.SetDouble("WindowHeight", ActualHeight);
            ls.Store.SetDouble("WindowLeft", Left);
            ls.Store.SetDouble("WindowTop", Top);
        }
        else if (WindowState == WindowState.Maximized)
        {
            ls.Store.SetDouble("WindowWidth", RestoreBounds.Width);
            ls.Store.SetDouble("WindowHeight", RestoreBounds.Height);
            ls.Store.SetDouble("WindowLeft", RestoreBounds.Left);
            ls.Store.SetDouble("WindowTop", RestoreBounds.Top);
        }

        ls.Store.Save();
    }
    // ============ 4.2 页面切换动画 ============

    private void AnimateToolChange()
    {
        if (ToolContentPanel == null) return;

        // 1. 清除旧动画
        ToolContentPanel.BeginAnimation(UIElement.OpacityProperty, null);
        ToolContentTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        // 2. ★ 关键：先把基础值设为最终状态
        ToolContentPanel.Opacity = 1;
        ToolContentTranslate.Y = 0;

        bool isSlowMode = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        int durationMs = isSlowMode ? 2200 : 220; // 按 Shift 慢放 10 倍

        // 3. 动画设定：From 从起始状态开始，To 到最终状态，结束后回到基础值（1 和 0）
        var fadeAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        ToolContentPanel.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        var slideAnim = new DoubleAnimation
        {
            From = 8,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        ToolContentTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
    }
    // ============ 自定义标题栏按钮 ============

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        else
            WindowState = WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}