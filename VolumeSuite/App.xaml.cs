using H.NotifyIcon;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using VolumeSuite.Services;

namespace VolumeSuite;

public partial class App : Application
{
    private static Mutex? _mutex;
    private TaskbarIcon? _trayIcon;
    private MainWindow? _mainWindow;

    private const string MutexName = "VolumeSuite_SingleInstance_Mutex";
    private const string PipeName = "VolumeSuite_SingleInstance_Pipe";

    protected override void OnStartup(StartupEventArgs e)
    {

        // ★ 1. 拦截 UI 线程异常（比如点击按钮报错）
        DispatcherUnhandledException += (s, ev) =>
        {
            LogHelper.WriteError("UI 线程异常", ev.Exception.ToString());
            MessageBox.Show($"发生错误：{ev.Exception.Message}\n详细信息已写入 logs 目录。", "程序错误", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true; // 告诉系统：我处理了，别闪退！
        };

        // ★ 2. 拦截后台线程异常（比如异步任务报错）
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            var ex = ev.ExceptionObject as Exception;
            LogHelper.WriteError("后台线程异常", ex?.ToString() ?? "未知错误");
        };

        // ★ 3. 拦截 Task 异常（如果任务没有被 await 可能会触发）
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ev) =>
        {
            LogHelper.WriteError("Task 异常", ev.Exception.ToString());
            ev.SetObserved();
        };

        _mutex = new Mutex(true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            // 已有实例，通知它弹窗口，自己退出
            TryActivateFirstInstance();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        StartPipeServer();

        // 从软件资源里读取你自己的 ico
        var iconUri = new Uri("pack://application:,,,/VolumeSuite.ico");
        var iconStream = Application.GetResourceStream(iconUri)?.Stream;

        _trayIcon = new TaskbarIcon
        {
            Icon = iconStream != null ? new System.Drawing.Icon(iconStream) : System.Drawing.SystemIcons.Application,
            ToolTipText = "VolumeSuite",
            Visibility = Visibility.Visible
        };
        _trayIcon.ForceCreate();
        _trayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();

        var menu = new System.Windows.Controls.ContextMenu();
        var openItem = new System.Windows.Controls.MenuItem { Header = "打开" };
        openItem.Click += (_, _) => ShowMainWindow();
        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(exitItem);
        _trayIcon.ContextMenu = menu;

        ShowMainWindow();
    }

    /// <summary>
    /// 第一个实例启动时调用：开一个后台线程循环监听管道连接。
    /// </summary>
    private void StartPipeServer()
    {
        Task.Run(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    string msg = reader.ReadToEnd();
                    if (msg == "activate")
                    {
                        Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                    }
                }
                catch { /* 忽略错误，继续监听 */ }
            }
        });
    }

    /// <summary>
    /// 第二个实例启动时调用：往管道写消息，让第一个实例弹窗口。
    /// </summary>
    private static void TryActivateFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client);
            writer.Write("activate");
            writer.Flush();
        }
        catch { }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
            _mainWindow = new MainWindow();

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();

        // 破解"窗口被其他程序挡在前面"的情况：先置顶再取消置顶
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    private void ExitApp()
    {
        _trayIcon?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}