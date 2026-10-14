using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
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
}