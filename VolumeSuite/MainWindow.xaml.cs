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

namespace VolumeSuite;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }
    private HotkeyItemViewModel? _capturingHotkey;
    private readonly List<string> _captureKeys = new();
    private System.Windows.Threading.DispatcherTimer? _windowSaveTimer;
    private bool _isRestoringWindow = false; // 防止恢复时误触发保存
    private System.Windows.Threading.DispatcherTimer? _smoothScrollTimer;
    private double _targetOffset = 0;
    private double _currentOffset = 0;

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

        ApplyTextEffects();

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

            ApplyAccentColor();
            ApplyTextEffects();
            ApplyBackground();
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
            ApplyAccentColor();
            ApplyWindowCorner();
            RestoreWindowPlacement();
            ApplyTextEffects();

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

    private void RefreshTools_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshTools();
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
                FileName = File.Exists(pvm.Value) ? Path.GetFileName(pvm.Value) : "",
                InitialDirectory = File.Exists(pvm.Value) ? Path.GetDirectoryName(pvm.Value) : "",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                string originalPath = dialog.FileName;

                // ★ 如果当前选的是背景图，复制到软件内部
                if (pvm.Key == "BackgroundImagePath")
                {
                    string? newPath = CopyImageToInternal(originalPath);
                    if (newPath != null)
                        pvm.Value = newPath;
                }
                else
                {
                    pvm.Value = originalPath;
                }
            }
        }
    }

    // ============ 3.4 复制图片到内部目录 ============

    private string? CopyImageToInternal(string sourcePath)
    {
        try
        {
            // 1. 确定内部目录：软件根目录/LauncherData/Backgrounds
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string bgDir = Path.Combine(appDir, "LauncherData", "Backgrounds");

            if (!Directory.Exists(bgDir))
                Directory.CreateDirectory(bgDir);

            // 2. 生成带时间戳的新文件名（如 20251024_153022.png）
            string ext = Path.GetExtension(sourcePath);
            string fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            string destPath = Path.Combine(bgDir, fileName);

            // 3. 复制文件
            File.Copy(sourcePath, destPath, true);

            // 4. 返回新的绝对路径（或者相对路径，这里为了兼容之前的代码，返回绝对路径）
            return destPath;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"图片复制失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
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
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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
        if (RightContentArea == null) return;

        RightContentArea.BeginAnimation(UIElement.OpacityProperty, null);
        RightContentTranslate.BeginAnimation(TranslateTransform.YProperty, null);

        RightContentArea.Opacity = 1;
        RightContentTranslate.Y = 0;

        bool isSlowMode = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        int durationMs = isSlowMode ? 2200 : 220;

        var fadeAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        RightContentArea.BeginAnimation(UIElement.OpacityProperty, fadeAnim);

        var slideAnim = new DoubleAnimation
        {
            From = 8,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        RightContentTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
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
    private void ApplyAccentColor()
    {
        var store = ViewModel?.LauncherSettings?.Store;
        if (store == null) return;

        var accentHex = store.GetString("AccentColor", "#5B8DEF");

        // ★ 就是这一行，确保它存在并且拼写正确
        var accentColor = ParseHexColor(accentHex, System.Windows.Media.Color.FromRgb(0x5B, 0x8D, 0xEF));

        // 直接更新颜色资源，DynamicResource 会自动刷新
        Resources["AccentColor"] = accentColor;
    }

    private void ApplyTextEffects()
    {
        var store = ViewModel?.LauncherSettings?.Store;
        if (store == null) return;

        string mode = store.GetString("TextEffectMode", "None");
        double blur = store.GetDouble("TextEffectBlur", 8);
        double opacity = store.GetDouble("TextEffectOpacity", 60) / 100.0;
        bool useAccent = store.GetBool("AccentGlow", false);

        // 颜色基础设置
        System.Windows.Media.Color glowColor = System.Windows.Media.Colors.White;
        if (useAccent)
            glowColor = ParseHexColor(store.GetString("AccentColor", "#5B8DEF"), System.Windows.Media.Colors.Blue);

        // 1. 定义全局文字特效（用来给除了标题以外的所有文字）
        var globalEffect = new System.Windows.Media.Effects.DropShadowEffect { Opacity = 0 }; // 默认无

        if (mode == "Shadow" || mode == "ShadowAndTitle")
        {
            // 全局文字用阴影
            globalEffect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black, // 可以在这里改成其他颜色
                BlurRadius = blur,
                ShadowDepth = 1,
                Opacity = opacity
            };
        }
        else if (mode == "Glow")
        {
            // 全局文字用辉光
            globalEffect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = glowColor,
                BlurRadius = blur,
                ShadowDepth = 0, // 不偏移，四周散开
                Opacity = opacity
            };
        }

        // 把全局特效塞给 DynamicResource，所有的 TextBlock 都会去拿这个
        Resources["GlobalTextShadow"] = globalEffect;

        // 2. 处理大标题专属的底层辉光（GlowText）
        if (GlowText != null)
        {
            if (mode == "Glow" || mode == "TitleOnly" || mode == "ShadowAndTitle")
            {
                // 如果是辉光相关模式，显示标题底层的发光层
                GlowText.Foreground = new System.Windows.Media.SolidColorBrush(glowColor);
                GlowText.Effect = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = blur,
                    KernelType = System.Windows.Media.Effects.KernelType.Gaussian,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality
                };
                GlowText.Opacity = opacity;
            }
            else if (mode == "Shadow")
            {
                // 如果选的是纯阴影，标题不要辉光层，让标题的字直接去吃 GlobalTextShadow 的阴影
                GlowText.Opacity = 0;
            }
            else
            {
                // 关
                GlowText.Opacity = 0;
            }
        }
    }

    // ============ 3.3 缩略图点击切换 ============

    private void Thumbnail_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string imagePath)
        {
            var ls = ViewModel?.LauncherSettings;
            if (ls == null) return;

            PathItemViewModel? targetPvm = null;
            foreach (var section in ls.Sections)
                foreach (var item in section.Items)
                    if (item is PathItemViewModel pvm && pvm.Key == "BackgroundImagePath")
                    {
                        targetPvm = pvm;
                        break;
                    }

            if (targetPvm == null) return;

            // ★ 1. 检查文件是否存在
            if (!File.Exists(imagePath))
            {
                MessageBox.Show("这张图片已经被删除了，将自动从列表中移除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                targetPvm.RefreshImages(); // 刷新列表，剔除不存在的文件
                return;
            }

            // 2. 正常切换
            targetPvm.Value = imagePath;
        }
    }
    private System.Windows.Threading.DispatcherTimer? _scrollTimer;


    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        e.Handled = true;

        // 初始化滚动值
        if (_smoothScrollTimer == null || !_smoothScrollTimer.IsEnabled)
        {
            _currentOffset = sv.VerticalOffset;
            _targetOffset = sv.VerticalOffset;
        }

        // 累加滚轮距离（如果觉得滚得慢，改这里的 1.0，比如 1.5 或 2.0）
        _targetOffset -= (e.Delta * 1.0);

        // 边界限制
        if (_targetOffset < 0) _targetOffset = 0;
        if (_targetOffset > sv.ScrollableHeight) _targetOffset = sv.ScrollableHeight;

        // 如果没有在滚动，启动动画循环
        if (_smoothScrollTimer == null)
        {
            _smoothScrollTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(10) // 约 60帧/秒
            };

            _smoothScrollTimer.Tick += (s, args) =>
            {
                double diff = _targetOffset - _currentOffset;

                // 距离极小说明到站了
                if (Math.Abs(diff) < 0.5)
                {
                    _currentOffset = _targetOffset;
                    sv.ScrollToVerticalOffset(_currentOffset);
                    _smoothScrollTimer.Stop();
                    return;
                }

                // 核心缓动公式：每次追向目标的 20%（越小越平滑，越大越快）
                _currentOffset += diff * 0.2;
                sv.ScrollToVerticalOffset(_currentOffset);
            };
        }

        _smoothScrollTimer.Start();
    }


    /*
    private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        e.Handled = true;

        // 单次滚动的距离调大（调这个数值决定滚多快）
        double scrollDistance = e.Delta * 1.5;

        // 如果是第一次滚，或者当前没在滚动，以当前位置为基准
        if (_scrollTimer == null || !_scrollTimer.IsEnabled)
            _targetOffset = sv.VerticalOffset;

        _targetOffset -= scrollDistance;

        // 限制边界
        if (_targetOffset < 0) _targetOffset = 0;
        if (_targetOffset > sv.ScrollableHeight) _targetOffset = sv.ScrollableHeight;

        // 如果 Timer 已经存在，直接 return，让现有的 Timer 去平滑过渡
        if (_scrollTimer != null && _scrollTimer.IsEnabled) return;

        _scrollTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        _scrollTimer.Tick += (s, args) =>
        {
            double current = sv.VerticalOffset;
            double diff = _targetOffset - current;

            if (Math.Abs(diff) < 1.0)
            {
                sv.ScrollToVerticalOffset(_targetOffset);
                _scrollTimer.Stop();
                return;
            }

            // 每次靠近目标的 15%，实现丝滑缓动
            sv.ScrollToVerticalOffset(current + diff * 0.15);
        };
        _scrollTimer.Start();
    }
    */


    // ============ 侧栏：右键菜单 ============

    private void SideItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ToolViewModel vm)
            return;

        var menu = new ContextMenu();

        // 1. 打开配置目录
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

        // 2. 重启工具
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

        // 3. 刷新工具列表（刚刚加的）
        var refreshItem = new MenuItem { Header = "刷新工具列表" };
        refreshItem.Click += (_, _) =>
        {
            ViewModel.RefreshTools();
        };
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        // 4. 更多设置
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

}
