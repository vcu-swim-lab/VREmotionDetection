using System;
using System.Diagnostics;

internal static class AdbRunner
{
    internal const int DefaultTimeoutMs = 20000;
    internal const int PullTimeoutMs = 300000;

    internal static (bool ok, string output) Run(string file, string arguments, int timeoutMs = DefaultTimeoutMs)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                return (false, "adb did not answer in time.");
            }

            return (process.ExitCode == 0, stdout.Result + stderr.Result);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }
}
