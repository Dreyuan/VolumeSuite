using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace VolumeSuite.Services;

/// <summary>
/// 读写某个工具的 settings.json。
/// 用 JsonObject 存值，schema 里的 key 直接映射到 JSON 属性名。
/// </summary>
public class SettingsStore
{
    private readonly string _path;
    private JsonObject _data = new();

    public SettingsStore(string path)
    {
        _path = path;
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                string json = File.ReadAllText(_path);
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                    _data = obj;
            }
        }
        catch
        {
            _data = new JsonObject();
        }
    }

    public void Save()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()   // ★ 加这一行
        };
        File.WriteAllText(_path, _data.ToJsonString(options));
    }

    // ========== 读取 ==========

    public bool GetBool(string key, bool defaultValue = false)
    {
        try { return _data[key]?.GetValue<bool>() ?? defaultValue; }
        catch { return defaultValue; }
    }

    public double GetDouble(string key, double defaultValue = 0)
    {
        try { return _data[key]?.GetValue<double>() ?? defaultValue; }
        catch { return defaultValue; }
    }

    public string GetString(string key, string defaultValue = "")
    {
        try { return _data[key]?.GetValue<string>() ?? defaultValue; }
        catch { return defaultValue; }
    }

    /// <summary>
    /// 读热键：settings.json 里存的是 ["ControlKey", "Menu", "Up"] 这种字符串数组。
    /// </summary>
    public List<string> GetHotkey(string key)
    {
        var result = new List<string>();
        try
        {
            if (_data[key] is JsonArray arr)
            {
                foreach (var node in arr)
                {
                    string? s = node?.GetValue<string>();
                    if (!string.IsNullOrEmpty(s)) result.Add(s);
                }
            }
        }
        catch { }
        return result;
    }

    // ========== 写入 ==========

    public void SetBool(string key, bool value) => _data[key] = value;
    public void SetDouble(string key, double value) => _data[key] = value;
    public void SetString(string key, string value) => _data[key] = value;

    public void SetHotkey(string key, IEnumerable<string> keys)
    {
        var arr = new JsonArray();
        foreach (var k in keys) arr.Add(k);
        _data[key] = arr;
    }
}