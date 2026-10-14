using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace VolumeSuite.Models;

public class ToolManifest
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Glyph { get; set; } = "Circle";
    public string Executable { get; set; } = "";
    public SettingsSchema? Settings { get; set; }

    [JsonIgnore]
    public string Directory { get; set; } = "";

    [JsonIgnore]
    public string ExePath => Path.Combine(Directory, Executable);

    [JsonIgnore]
    public string SettingsPath =>
    Id == "_launcher"
        ? Path.Combine(Directory, "launcher.json")
        : Path.Combine(Directory, "settings.json");
}

public class SettingsSchema
{
    public List<SettingsSection> Sections { get; set; } = new();
}

public class SettingsSection
{
    public string Title { get; set; } = "";
    public List<SettingsItem> Items { get; set; } = new();
}

public class SettingsItem
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "string";
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public int? Decimals { get; set; }
    public double? Curve { get; set; }
    public string? Mode { get; set; }
    public string? Source { get; set; }   // ★ dropdown 的候选来源
    public List<DropdownOption>? StaticOptions { get; set; }
}