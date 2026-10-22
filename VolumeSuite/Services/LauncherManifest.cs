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
                                    new() { Id = "acrylic", Name = "毛玻璃" },
                                }
                            },
                            new()
                            {
                                Key = "WindowCornerRadius",
                                Label = "窗口圆角半径 (px)",
                                Type = "number",
                                Min = 0,
                                Max = 30,
                                Step = 1,
                                Decimals = 0,
                                Default = 12,
                            },
                            new()
                            {
                                Key = "TitleBarTint",
                                Label = "标题栏色调",
                                Type = "dropdown",
                                StaticOptions = new List<DropdownOption>
                                {
                                    new() { Id = "follow", Name = "跟随背景" },
                                    new() { Id = "half",   Name = "半透明黑" },
                                    new() { Id = "solid",  Name = "不透明黑" },
                                }
                            },
                            new()
                            {
                                Key = "BackgroundImagePath",
                                Label = "背景图片",
                                Type = "path",
                                Mode = "file",
                            },
                            new()
                            {
                                Key = "AcrylicTintColor",
                                Label = "毛玻璃色调",
                                Type = "color",
                            },
                            new()
                            {
                                Key = "AcrylicTintOpacity",
                                Label = "毛玻璃色调深度 (%)",
                                Type = "number",
                                Min = 0,
                                Max = 100,
                                Step = 1,
                                Decimals = 0,
                                Default = 20,
                            },
                            new()
                            {
                                Key = "ImageOpacity",
                                Label = "图片不透明度 (%)",
                                Type = "number",
                                Min = 0,
                                Max = 100,
                                Step = 1,
                                Decimals = 0,
                                Default = 40,
                            },
                            new()
                            {
                                Key = "ImageBlurRadius",
                                Label = "图片模糊半径",
                                Type = "number",
                                Min = 0,
                                Max = 30,
                                Step = 1,
                                Decimals = 0,
                                Default = 0,
                            },
                            new()
                            {
                                Key = "ImageShadeDepth",
                                Label = "图片遮罩深度 (%)",
                                Type = "number",
                                Min = 0,
                                Max = 100,
                                Step = 1,
                                Decimals = 0,
                                Default = 60,
                            },
                            new()
                            {
                                Key = "AccentColor",
                                Label = "软件强调色",
                                Type = "color",
                            },
                            new()
                            {
                                Key = "EnableTextShadow",
                                Label = "启用文字阴影（增加可读性）",
                                Type = "bool",
                            },
                        }
                    }
                }
            }
        };
    }
}