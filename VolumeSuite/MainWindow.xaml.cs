using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using VolumeSuite.Services;
using VolumeSuite.ViewModels;
using WpfUi = Wpf.Ui.Controls;   // ← 报错说这个命名空间不存在
namespace VolumeSuite;

public partial class MainWindow : WpfUi.FluentWindow
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

        // ★ 关键：延迟到 Loaded，此时 WPF-UI 已经初始化完，不会被覆盖
        Loaded += (_, _) =>
        {
            var source = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor =
                    System.Windows.Media.Colors.Transparent;

            ApplyBackground();
            source?.AddHook(WndProc);
        };
        // 窗口每次重新激活（比如下拉框关闭、切回本窗口）时，
        // 强制重新应用一次毛玻璃——防止 WPF-UI 抢在前面覆盖掉
        Activated += (_, _) =>
        {
            var ls = ViewModel?.LauncherSettings;
            if (ls == null) return;

            if (ls.Store.GetString("BackgroundMode", "image") == "acrylic")
                AcrylicHelper.EnableAcrylic(this, 0x00000000);
        };
    }

    /// <summary>
    /// 点 X 时只隐藏窗口，不真的关闭。
    /// </summary>
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

    /// <summary>
    /// 左下角"启动器设置"按钮
    /// </summary>
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

    // ============ 背景：根据启动器设置应用 ============

    private void ApplyBackground()
    {
        if (ViewModel.LauncherSettings == null) return;

        var store = ViewModel.LauncherSettings.Store;
        var mode = store.GetString("BackgroundMode", "image");
        var path = store.GetString("BackgroundImagePath", "");

        if (mode == "acrylic")
        {
            // ===== 毛玻璃模式 =====
            // 隐藏不透明底和图片，让 DWM 模糊透出来
            if (OpaqueUnderlay != null)
                OpaqueUnderlay.Visibility = Visibility.Collapsed;

            if (BackgroundImage != null)
                BackgroundImage.Visibility = Visibility.Collapsed;

            // 色调层
            // 色调层（诊断：暂时隐藏）
            if (BackgroundShade != null)
            {
                BackgroundShade.Visibility = Visibility.Collapsed;
            }

            if (AppTitleBar != null)
                AppTitleBar.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0x66, 0x00, 0x00, 0x00));

            AcrylicHelper.EnableAcrylic(this, 0x00000000);
            return;
        }

        // ===== 图片模式 =====
        AcrylicHelper.DisableAcrylic(this);

        // 显示不透明底，遮住 DWM 的模糊
        if (OpaqueUnderlay != null)
            OpaqueUnderlay.Visibility = Visibility.Visible;

        if (BackgroundImage != null)
            BackgroundImage.Visibility = Visibility.Visible;

        if (BackgroundShade != null)
        {
            BackgroundShade.Visibility = Visibility.Visible;
            BackgroundShade.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(0x99, 0x00, 0x00, 0x00));
        }

        if (AppTitleBar != null)
            AppTitleBar.Background = System.Windows.Media.Brushes.Transparent;

        BitmapImage? bmp = null;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            bmp = LoadBitmapFromFile(path);
        if (bmp == null)
            bmp = LoadBitmapFromResource("pack://application:,,,/background.png");
        if (BackgroundImage != null && bmp != null)
            BackgroundImage.Source = bmp;
    }

    /// <summary>
    /// 加载图片，兼容带 EXIF/ICC 元数据的"问题图片"。
    /// 先用 WPF 直接加载，失败则用 GDI+ 转码剥离元数据后重试。
    /// </summary>
    private static BitmapImage? LoadBitmapFromFile(string path)
    {
        // 快路径：直接 WPF 加载
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
        catch
        {
            // 落到慢路径
        }

        // 慢路径：GDI+ 解码 + 重新编码为 PNG，剥离元数据
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

    /// <summary>
    /// 从 pack:// 资源路径加载 BitmapImage
    /// </summary>
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
    // ============ 拦截窗口激活消息，抢占式重设模糊 ============

    private const int WM_NCACTIVATE = 0x0086;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCACTIVATE)
        {
            // 用最低优先级排队，保证在 WPF-UI 处理完之后再执行
            Dispatcher.BeginInvoke(
                new Action(ReapplyAcrylicIfNeeded),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        return IntPtr.Zero;
    }

    private void ReapplyAcrylicIfNeeded()
    {
        var ls = ViewModel?.LauncherSettings;
        if (ls == null) return;
        if (ls.Store.GetString("BackgroundMode", "image") != "acrylic") return;

        AcrylicHelper.EnableAcrylic(this, 0x00000000);
    }
}