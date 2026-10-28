using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using VolumeSuite.Services;

namespace VolumeSuite.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<ToolViewModel> Tools { get; } = new();
    public ToolViewModel LauncherSettings { get; }

    [ObservableProperty] private ToolViewModel? _selectedTool;

    private readonly DispatcherTimer _syncTimer;

    public MainViewModel()
    {
        foreach (var manifest in ToolScanner.Scan())
            Tools.Add(new ToolViewModel(manifest));

        var launcherManifest = LauncherManifest.Create();
        var launcherStore = new SettingsStore(launcherManifest.SettingsPath);

        // 注入默认值（只在新文件时写入，不覆盖用户修改）
        if (string.IsNullOrEmpty(launcherStore.GetString("BackgroundMode")))
            launcherStore.SetString("BackgroundMode", "image");
        if (string.IsNullOrEmpty(launcherStore.GetString("BackgroundImagePath")))
            launcherStore.SetString("BackgroundImagePath", "");

        launcherStore.Save();

        LauncherSettings = new ToolViewModel(launcherManifest);

        if (Tools.Count > 0)
            SelectedTool = Tools[0];

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _syncTimer.Tick += (_, _) => SyncAllStates();
        _syncTimer.Start();
        StartFileWatcher(); // ★ 必须加上这一行，启动监听
    }

    partial void OnSelectedToolChanged(ToolViewModel? value)
    {
        foreach (var t in Tools)
            t.IsSelected = ReferenceEquals(t, value);

        if (LauncherSettings != null)
            LauncherSettings.IsSelected = ReferenceEquals(LauncherSettings, value);
    }

    private void SyncAllStates()
    {
        foreach (var t in Tools)
            t.SyncRunningState();
    }

    // 重新扫描工具目录并刷新列表
    // 重新扫描工具目录并刷新列表
    public void RefreshTools()
    {
        try
        {
            // ★ 关键修复：直接调用静态方法，不需要 new
            var manifests = VolumeSuite.Services.ToolScanner.Scan();

            // 记录当前选中的工具 ID，刷新后尽量保持选中
            string currentId = SelectedTool?.Manifest?.Id ?? "";

            // 清空并重新填充 Tools 列表
            Tools.Clear();
            foreach (var m in manifests)
            {
                Tools.Add(new ToolViewModel(m));
            }

            // 恢复选中状态
            var target = Tools.FirstOrDefault(t => t.Manifest.Id == currentId);
            SelectedTool = target ?? Tools.FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"刷新工具失败: {ex.Message}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private FileSystemWatcher? _toolsWatcher;

    public void StartFileWatcher()
    {
        try
        {
            string toolsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            if (!System.IO.Directory.Exists(toolsDir)) System.IO.Directory.CreateDirectory(toolsDir);

            _toolsWatcher = new FileSystemWatcher(toolsDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                Filter = "*.*"
            };

            // 用 DispatcherTimer 防抖：文件系统变动时，不会瞬间刷新，而是等 1 秒内没有新变动才刷新
            System.Windows.Threading.DispatcherTimer? debounceTimer = null;

            void TriggerRefresh()
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (debounceTimer == null)
                    {
                        debounceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                        debounceTimer.Tick += (s, e) =>
                        {
                            debounceTimer.Stop();
                            RefreshTools(); // 刷新列表
                        };
                    }
                    debounceTimer.Stop();
                    debounceTimer.Start();
                });
            }

            _toolsWatcher.Changed += (s, e) => TriggerRefresh();
            _toolsWatcher.Created += (s, e) => TriggerRefresh();
            _toolsWatcher.Deleted += (s, e) => TriggerRefresh();
            _toolsWatcher.Renamed += (s, e) => TriggerRefresh();

            _toolsWatcher.EnableRaisingEvents = true;
        }
        catch { }
    }

}