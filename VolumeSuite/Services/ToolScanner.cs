using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VolumeSuite.Models;

namespace VolumeSuite.Services;

public static class ToolScanner
{
    /// <summary>
    /// 扫描 exe 同目录下的 tools\*\tool.json，返回所有工具。
    /// </summary>
    public static List<ToolManifest> Scan()
    {
        var result = new List<ToolManifest>();
        string toolsDir = Path.Combine(AppContext.BaseDirectory, "tools");

        if (!Directory.Exists(toolsDir))
            return result;

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        foreach (var dir in Directory.GetDirectories(toolsDir))
        {
            string manifestPath = Path.Combine(dir, "tool.json");
            if (!File.Exists(manifestPath))
                continue;

            try
            {
                string json = File.ReadAllText(manifestPath);
                var tool = JsonSerializer.Deserialize<ToolManifest>(json, options);
                if (tool == null || string.IsNullOrEmpty(tool.Id))
                    continue;

                tool.Directory = dir;
                result.Add(tool);
            }
            catch
            {
                // 单个工具的 tool.json 坏了，跳过它，不影响其他工具
            }
        }

        return result;
    }
}