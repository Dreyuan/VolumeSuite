using System;
using System.Collections.Generic;
using System.IO;
using VolumeSuite.Models;

namespace VolumeSuite.Services;

public static class LauncherManifest
{
    public static ToolManifest Create()
    {
        return new ToolManifest
        {
            Id = "_launcher",
            DisplayName = "启动器设置",
            Description = "VolumeSuite 自身的配置",
            Glyph = "Settings",
            Executable = Path.GetFileName(Environment.ProcessPath ?? "VolumeSuite.exe"),
            Directory = AppContext.BaseDirectory,
            Settings = new SettingsSchema
            {
                Sections = new List<SettingsSection>
                {
                    new()
                    {
                        Title = "外观",
                        Items = new List<SettingsItem>
                        {
                            new()
                            {
                                Key = "BackgroundMode",
                                Label = "背景模式",
                                Type = "dropdown",
                                StaticOptions = new List<DropdownOption>
                                {
                                    new() { Id = "image",   Name = "图片背景" },
                                    new() { Id = "acrylic", Name = "毛玻璃（实验性）" },
                                }
                            },
                            new()
                            {
                                Key = "BackgroundImagePath",
                                Label = "背景图片",
                                Type = "path",
                                Mode = "file",
                            },
                        }
                    }
                }
            }
        };
    }
}