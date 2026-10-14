using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace VolumeSuite.Services;

public static class ProcessRunner
{
    /// <summary>
    /// 执行一个进程并返回 stdout 内容（UTF-8 解码）。失败返回 null。
    /// </summary>
    public static async Task<string?> RunAsync(string exePath, string args, int timeoutMs = 5000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p == null) return null;

            string output = await p.StandardOutput.ReadToEndAsync();
            string error = await p.StandardError.ReadToEndAsync();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); } catch { }
                return null;
            }

            return p.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}