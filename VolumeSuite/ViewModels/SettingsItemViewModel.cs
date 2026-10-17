using ColorPicker.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Xml.Linq;
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

    public static event System.Action<string>? AnySettingsChanged;

    /// <summary>保存到磁盘 + 通知</summary>
    protected void Persist()
    {
        Owner.SaveSettings();
        AnySettingsChanged?.Invoke(Key);
    }

    /// <summary>只通知，不落盘（拖动中用）</summary>
    protected void NotifyOnly()
    {
        AnySettingsChanged?.Invoke(Key);
    }

    public static SettingsItemViewModel Create(SettingsItem schema, ToolViewModel owner)
    {
        return schema.Type switch
        {
            "bool" => new BoolItemViewModel(schema, owner),
            "number" => new NumberItemViewModel(schema, owner),
            "hotkey" => new HotkeyItemViewModel(schema, owner),
            "path" => new PathItemViewModel(schema, owner),
            "dropdown" => new DropdownItemViewModel(schema, owner),
            "color" => new ColorItemViewModel(schema, owner),
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
        Min = schema.Min ?? 0;
        Max = schema.Max ?? 100;
        Step = schema.Step ?? 1;
        Decimals = schema.Decimals ?? 0;
        Curve = schema.Curve ?? 1.0;

        // ★ 如果 store 里没这个 key，先写内存（不落盘），保证 UI 和 ApplyBackground 看到同一个值
        double def = schema.Default ?? Min;
        if (!owner.Store.HasKey(schema.Key))
            owner.Store.SetDouble(schema.Key, def);

        _value = owner.Store.GetDouble(schema.Key, def);
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

        Owner.Store.SetDouble(Key, value);   // ★ 内存永远实时更新

        if (_isDragging)
        {
            NotifyOnly();                     // ★ 拖动中：通知但不落盘
            return;
        }

        Persist();                            // 松手 / 输入框改：落盘
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

    private readonly string _mode;

    /// <summary>供 TextBox 绑定——setter 里做校验，非法输入不写盘</summary>
    public string Text
    {
        get => Value;
        set
        {
            if (!IsAcceptable(value))
            {
                OnPropertyChanged();   // 触发 UI 回滚到原值
                return;
            }
            Value = value ?? "";
        }
    }

    public TextItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        _value = owner.Store.GetString(schema.Key);
        _mode = schema.Mode ?? "";
    }

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(Text));
        Owner.Store.SetString(Key, value ?? "");
        Persist();
    }

    private bool IsAcceptable(string? s)
    {
        if (_mode == "hex") return IsValidHex(s);
        return true;
    }

    private static bool IsValidHex(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim().TrimStart('#');
        if (s.Length != 6 && s.Length != 8) return false;
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
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

        // 如果没有存储值，用第一个 StaticOptions 的 Id 作为默认
        if (owner.Store.HasKey(schema.Key))
        {
            _currentValue = owner.Store.GetString(schema.Key);
        }
        else if (schema.StaticOptions != null && schema.StaticOptions.Count > 0)
        {
            _currentValue = schema.StaticOptions[0].Id;
        }
        else
        {
            _currentValue = "";
        }

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

public partial class ColorSlot : ObservableObject
{
    [ObservableProperty] private string _hex = "";
    [ObservableProperty] private System.Windows.Media.Brush? _brush;

    public int Index { get; set; }
    public ColorItemViewModel? Parent { get; set; }

    public bool IsEmpty => string.IsNullOrEmpty(Hex);
    public bool IsFilled => !IsEmpty;

    partial void OnHexChanged(string value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsFilled));

        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value);
                var b = new System.Windows.Media.SolidColorBrush(c);
                b.Freeze();
                Brush = b;
            }
            catch { Brush = null; }
        }
        else
        {
            Brush = null;
        }
    }
}
public partial class ColorItemViewModel : SettingsItemViewModel
{
    private System.Windows.Media.Color _value;
    private bool _isExpanded;

    private static readonly string[] DefaultCommonColors = new[]
    {
        "#000000", "#FFFFFF", "#5B8DEF", "#FF5555",
        "#FFAA00", "#FFEB3B", "#4CAF50", "#00BCD4",
        "#9C27B0", "#E91E63", "#795548", "#607D8B"
    };

    public System.Collections.ObjectModel.ObservableCollection<ColorSlot> CommonColors { get; } = new();
    public System.Collections.ObjectModel.ObservableCollection<ColorSlot> SavedColors { get; } = new();

    public System.Windows.Media.Color Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HexText));
            OnPropertyChanged(nameof(ValueAsBrush));
            OnPropertyChanged(nameof(RgbText));

            Owner.Store.SetString(Key, HexText);
            Persist();
        }
    }

    public System.Windows.Media.Brush ValueAsBrush
    {
        get
        {
            var b = new System.Windows.Media.SolidColorBrush(Value);
            b.Freeze();
            return b;
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); }
    }

    public string HexText
    {
        get => $"#{Value.R:X2}{Value.G:X2}{Value.B:X2}";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) { OnPropertyChanged(); return; }
            var s = value.Trim().TrimStart('#');
            if (s.Length != 6 && s.Length != 8) { OnPropertyChanged(); return; }
            try
            {
                int offset = s.Length == 8 ? 2 : 0;
                byte r = Convert.ToByte(s.Substring(offset + 0, 2), 16);
                byte g = Convert.ToByte(s.Substring(offset + 2, 2), 16);
                byte b = Convert.ToByte(s.Substring(offset + 4, 2), 16);
                Value = System.Windows.Media.Color.FromRgb(r, g, b);
            }
            catch { OnPropertyChanged(); }
        }
    }

    public string RgbText => $"{Value.R}, {Value.G}, {Value.B}";

    public ColorItemViewModel(SettingsItem schema, ToolViewModel owner) : base(schema, owner)
    {
        var hex = owner.Store.GetString(schema.Key, "#000000");
        _value = ParseHexToColor(hex);

        // 常用色板
        foreach (var h in DefaultCommonColors)
            CommonColors.Add(new ColorSlot { Hex = h, Parent = this });

        // 收藏夹
        var saved = owner.Store.GetStringList("SavedColors");
        while (saved.Count < 12) saved.Add("");
        for (int i = 0; i < 12; i++)
            SavedColors.Add(new ColorSlot { Hex = saved[i], Parent = this, Index = i });
    }

    public void ApplyHex(string hex)
    {
        HexText = hex;
    }

    public void SaveCurrentToSlot(int index)
    {
        if (index < 0 || index >= SavedColors.Count) return;
        SavedColors[index].Hex = HexText;
        PersistSaved();
    }

    public void ClearSlot(int index)
    {
        if (index < 0 || index >= SavedColors.Count) return;
        SavedColors[index].Hex = "";
        PersistSaved();
    }

    private void PersistSaved()
    {
        var list = new System.Collections.Generic.List<string>();
        foreach (var s in SavedColors) list.Add(s.Hex ?? "");
        Owner.Store.SetStringList("SavedColors", list);
        Owner.SaveSettings();
    }

    private static System.Windows.Media.Color ParseHexToColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return System.Windows.Media.Colors.Black;
        var s = hex.Trim().TrimStart('#');
        try
        {
            if (s.Length == 6)
            {
                return System.Windows.Media.Color.FromRgb(
                    Convert.ToByte(s.Substring(0, 2), 16),
                    Convert.ToByte(s.Substring(2, 2), 16),
                    Convert.ToByte(s.Substring(4, 2), 16));
            }
            if (s.Length == 8)
            {
                return System.Windows.Media.Color.FromRgb(
                    Convert.ToByte(s.Substring(2, 2), 16),
                    Convert.ToByte(s.Substring(4, 2), 16),
                    Convert.ToByte(s.Substring(6, 2), 16));
            }
        }
        catch { }
        return System.Windows.Media.Colors.Black;
    }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isPicking;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private System.Windows.Media.Brush? _pickPreviewBrush;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _pickPreviewHex = "";

    private System.Windows.Media.Color _originalColor;

    public void StartPick()
    {
        _originalColor = Value;
        IsPicking = true;
    }

    public void UpdatePick(System.Windows.Media.Color color)
    {
        var b = new System.Windows.Media.SolidColorBrush(color);
        b.Freeze();
        PickPreviewBrush = b;
        PickPreviewHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    public void CommitPick(System.Windows.Media.Color color)
    {
        IsPicking = false;
        Value = color; // 这会触发绑定更新色相环

        // 强制通知一次状态变化，确保界面收起
        OnPropertyChanged(nameof(IsPicking));
    }

    public void CancelPick()
    {
        IsPicking = false;
        Value = _originalColor;
    }
}