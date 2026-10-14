using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using VolumeSuite.Models;
using VolumeSuite.Services;

namespace VolumeSuite.ViewModels;

public abstract partial class SettingsItemViewModel : ObservableObject
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    protected ToolViewModel Owner { get; }

    protected SettingsItemViewModel(SettingsItem schema, ToolViewModel owner)
    {
        Key = schema.Key;
        Label = schema.Label;
        Owner = owner;
    }

    protected void Persist() => Owner.SaveSettings();

    public static SettingsItemViewModel Create(SettingsItem schema, ToolViewModel owner)
    {
        return schema.Type switch
        {
            "bool" => new BoolItemViewModel(schema, owner),
            "number" => new NumberItemViewModel(schema, owner),
            "hotkey" => new HotkeyItemViewModel(schema, owner),
            "path" => new PathItemViewModel(schema, owner),
            "dropdown" => new DropdownItemViewModel(schema, owner),
            _ => new TextItemViewModel(schema, owner),
        };
    }
}

public partial class BoolItemViewModel : SettingsItemViewModel
{
    [ObservableProperty] private bool _value;

    public BoolItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        _value = owner.Store.GetBool(schema.Key);
    }

    partial void OnValueChanged(bool value)
    {
        Owner.Store.SetBool(Key, value);
        Persist();
    }
}

public partial class NumberItemViewModel : SettingsItemViewModel
{
    [ObservableProperty] private double _value;
    [ObservableProperty] private double _position;

    private bool _isDragging;
    private bool _suppressSync;

    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public int Decimals { get; }
    public double Curve { get; }

    public string Text
    {
        get => Value.ToString("F" + Decimals);
        set
        {
            if (double.TryParse(value, out var v))
                Value = Math.Clamp(v, Min, Max);
            else
                OnPropertyChanged();
        }
    }

    public NumberItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        _value = owner.Store.GetDouble(schema.Key);
        Min = schema.Min ?? 0;
        Max = schema.Max ?? 100;
        Step = schema.Step ?? 1;
        Decimals = schema.Decimals ?? 0;
        Curve = schema.Curve ?? 1.0;
        _position = ValueToPosition(_value);
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(Text));

        if (!_suppressSync)
        {
            _suppressSync = true;
            Position = ValueToPosition(value);
            _suppressSync = false;
        }

        if (_isDragging) return;
        Owner.Store.SetDouble(Key, value);
        Persist();
    }

    partial void OnPositionChanged(double value)
    {
        if (_suppressSync) return;
        _suppressSync = true;
        Value = PositionToValue(value);
        _suppressSync = false;
    }

    private double ValueToPosition(double v)
    {
        if (Max <= Min) return 0;
        var t = (v - Min) / (Max - Min);
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return Curve == 1.0 ? t : Math.Pow(t, 1.0 / Curve);
    }

    private double PositionToValue(double p)
    {
        if (p < 0) p = 0;
        if (p > 1) p = 1;
        var t = Curve == 1.0 ? p : Math.Pow(p, Curve);
        return Min + (Max - Min) * t;
    }

    public void BeginDrag() => _isDragging = true;

    public void EndDrag()
    {
        if (!_isDragging) return;
        _isDragging = false;
        Owner.Store.SetDouble(Key, Value);
        Persist();
    }
}

public partial class TextItemViewModel : SettingsItemViewModel
{
    [ObservableProperty] private string _value = "";

    public TextItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        _value = owner.Store.GetString(schema.Key);
    }

    partial void OnValueChanged(string value)
    {
        Owner.Store.SetString(Key, value ?? "");
        Persist();
    }
}

public partial class HotkeyItemViewModel : SettingsItemViewModel
{
    [ObservableProperty] private string _display = "";

    private List<string> _keys = new();

    public HotkeyItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        _keys = owner.Store.GetHotkey(schema.Key);
        _display = FormatKeys(_keys);
    }

    private static string FormatKeys(List<string> keys)
    {
        if (keys == null || keys.Count == 0) return "未设置";
        var parts = new List<string>();
        foreach (var k in keys)
        {
            parts.Add(k switch
            {
                "ControlKey" or "LControlKey" or "RControlKey" => "Ctrl",
                "ShiftKey" or "LShiftKey" or "RShiftKey" => "Shift",
                "Menu" or "LMenu" or "RMenu" => "Alt",
                "LWin" or "RWin" => "Win",
                _ => k,
            });
        }
        return string.Join(" + ", parts);
    }

    public void UpdateKeys(List<string> keys)
    {
        _keys = keys;
        Display = FormatKeys(keys);
        Owner.Store.SetHotkey(Key, keys);
        Persist();
    }

    public void CancelCapture()
    {
        Display = FormatKeys(_keys);
    }
}

public partial class PathItemViewModel : SettingsItemViewModel
{
    [ObservableProperty] private string _value = "";

    public string Mode { get; }

    public PathItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        _value = owner.Store.GetString(schema.Key);
        Mode = schema.Mode ?? "file";
    }

    partial void OnValueChanged(string value)
    {
        Owner.Store.SetString(Key, value ?? "");
        Persist();
    }
}

public partial class DropdownItemViewModel : SettingsItemViewModel
{
    [ObservableProperty] private string _currentValue = "";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<DropdownOption> Options { get; } = new();

    private readonly string _source;
    private readonly string _exePath;

    public DropdownItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        _source = schema.Source ?? "";
        _exePath = owner.Manifest.ExePath;

        _currentValue = owner.Store.GetString(schema.Key);

        if (schema.StaticOptions != null && schema.StaticOptions.Count > 0)
        {
            foreach (var opt in schema.StaticOptions)
                Options.Add(opt);
        }
        else
        {
            _ = LoadOptionsAsync();
        }
    }

    private async Task LoadOptionsAsync()
    {
        if (string.IsNullOrEmpty(_source)) return;

        IsLoading = true;
        string args = $"--list-{_source}";
        string? json = await ProcessRunner.RunAsync(_exePath, args);
        IsLoading = false;

        if (string.IsNullOrEmpty(json)) return;

        try
        {
            var list = JsonSerializer.Deserialize<List<DropdownOption>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (list == null) return;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                Options.Clear();
                foreach (var opt in list) Options.Add(opt);
            });
        }
        catch { }
    }

    partial void OnCurrentValueChanged(string value)
    {
        Owner.Store.SetString(Key, value ?? "");
        Persist();
    }
}