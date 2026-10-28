using System;
using System.IO;

namespace VolumeSuite.Services;

public static class LogHelper
{
    private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

    public static void WriteError(string message, string exceptionDetail = "")
    {
        try
        {
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            string logFile = Path.Combine(LogDir, $"error_{DateTime.Now:yyyyMMdd}.log");
            string content = $"[{DateTime.Now:HH:mm:ss}] {message}\n{exceptionDetail}\n-------------------\n";
            File.AppendAllText(logFile, content);
        }
        catch { /* 如果连写日志都失败，那就静默忽略，防止死循环 */ }
    }
}