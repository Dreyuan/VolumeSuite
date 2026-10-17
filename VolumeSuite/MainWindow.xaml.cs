using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public MainWindow()
    {
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        InitializeComponent();

        PreviewKeyDown += MainWindow_PreviewKeyDown;

        ViewModel.LauncherSettings.SettingsChanged += ApplyBackground;

        // 启动器设置里任意一项变化时，重新应用背景
        SettingsItemViewModel.AnySettingsChanged += (key) =>
        {
            if (ViewModel?.SelectedTool != ViewModel?.LauncherSettings) return;

            // ★ 如果改的是 8 位 HEX 色调，同步 alpha 到滑条
            SyncTintOpacityFromHex(key);

            ApplyBackground();
        };

        Loaded += (_, _) =>
        {
            var source = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor =
                    System.Windows.Media.Colors.Transparent;

            ApplyBackground();
        };

        // ★ 最小化/恢复时控制缩略图是否图标化
        /*
         StateChanged += (_, _) =>
        {
            var ls = ViewModel?.LauncherSettings;
            if (ls == null) return;
            bool isAcrylic = ls.Store.GetString("BackgroundMode", "image") == "acrylic";

            if (WindowState == WindowState.Minimized)
            {
                if (isAcrylic)
                {
                    // 延迟 300ms 再图标化，让最小化动画用真实缩略图
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(300)
                    };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        SetIconicThumbnail(true);
                    };
                    timer.Start();
                }
                // 图片模式：不图标化，让 DWM 用正常缩略图
            }
            else
            {
                // 恢复：立即重设模糊，让动画期间就有模糊
                if (isAcrylic)
                    AcrylicHelper.EnableAcrylic(this, 0x00000000);

                SetIconicThumbnail(false);
            }
        };
        */
    }

    /// <summary>点 X 时只隐藏窗口，不真的关闭。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
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
}