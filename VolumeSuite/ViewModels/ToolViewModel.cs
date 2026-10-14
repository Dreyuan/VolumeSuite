using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using VolumeSuite.Models;
using VolumeSuite.Services;

namespace VolumeSuite.ViewModels;

public partial class ToolViewModel : ObservableObject
{
    public ToolManifest Manifest { get; }
    public bool IsLauncher { get; }
    public event Action? SettingsChanged;
    public SettingsStore Store { get; }
    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = new();

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isSelected;

    private Process? _process;

    public ToolViewModel(ToolManifest manifest)
    {
        Manifest = manifest;
        IsLauncher = manifest.Id == "_launcher";
        Store = new SettingsStore(manifest.SettingsPath);
        BuildSections();
        if (!IsLauncher) SyncRunningState();
    }

    private void BuildSections()
    {
        if (Manifest.Settings?.Sections == null) return;

        foreach (var section in Manifest.Settings.Sections)
        {
            var vm = new SettingsSectionViewModel { Title = section.Title };
            foreach (var item in section.Items)
                vm.Items.Add(SettingsItemViewModel.Create(item, this));
            Sections.Add(vm);
        }
    }

    public void Toggle()
    {
        if (IsRunning) Stop();
        else Start();
    }

    public void Start()
    {
        if (IsLauncher) return;
        if (IsRunning) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Manifest.ExePath,
                WorkingDirectory = Manifest.Directory,
                UseShellExecute = false
            };
            _process = Process.Start(psi);
            if (_process == null) return;
            IsRunning = true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"启动 {Manifest.DisplayName} 失败：{ex.Message}",
                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Stop()
    {
        if (IsLauncher) return;
        try
        {
            string processName = System.IO.Path.GetFileNameWithoutExtension(Manifest.Executable);
            var all = Process.GetProcessesByName(processName);
            foreach (var p in all)
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
                p.Dispose();
            }
        }
        catch { }

        _process = null;
        IsRunning = false;
    }

    /// <summary>
    /// 启动器启动时调用：如果工具已经在运行（比如开机自启的），
    /// 关联到那个进程，让 IsRunning 反映真实状态。
    /// </summary>
    /// <summary>
    /// 纯查询：扫进程，更新 IsRunning，关联 _process。
    /// 每 1.5 秒被 MainViewModel 调用一次。
    /// </summary>
    public void SyncRunningState()
    {
        if (IsLauncher) return;
        try
        {
            string processName = System.IO.Path.GetFileNameWithoutExtension(Manifest.Executable);
            var existing = Process.GetProcessesByName(processName);
            bool actuallyRunning = existing.Length > 0;

            // _process 已经退出了就丢掉
            if (_process != null && _process.HasExited)
                _process = null;

            // 状态对齐
            if (IsRunning != actuallyRunning)
                IsRunning = actuallyRunning;

            // 关联一个进程对象（用于 Kill）
            if (actuallyRunning && _process == null)
                _process = existing[0];
            else
            {
                // 多余的对象释放掉（避免句柄泄漏）
                for (int i = 1; i < existing.Length; i++)
                    existing[i].Dispose();
            }
        }
        catch { }
    }

    public void SaveSettings()
    {
        Store.Save();
        SettingsChanged?.Invoke();
    }
}

public class SettingsSectionViewModel
{
    public string Title { get; set; } = "";
    public ObservableCollection<SettingsItemViewModel> Items { get; } = new();
}