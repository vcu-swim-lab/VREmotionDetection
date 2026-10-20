using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace VcuSeCenter.VREmotionDetection
{
    internal static class AdbOutput
    {
        internal readonly struct Devices
        {
            internal Devices(string[] ready, bool unauthorized)
            {
                Ready = ready;
                Unauthorized = unauthorized;
            }

            internal string[] Ready { get; }

            internal bool Unauthorized { get; }
        }

        internal static Devices ParseDevices(string text)
        {
            var states = (text ?? string.Empty)
                .Split('\n')
                .Select(line => line.Trim().Split('\t'))
                .Where(parts => parts.Length == 2 && parts[0].Length > 0)
                .ToArray();

            return new Devices(
                states.Where(parts => parts[1] == "device").Select(parts => parts[0]).ToArray(),
                states.Any(parts => parts[1] == "unauthorized"));
        }

        internal static bool IsMissingRemoteFolder(string output)
        {
            return output != null && (output.Contains("does not exist") || output.Contains("No such file"));
        }

        internal static int? PulledFileCount(string output)
        {
            var match = Regex.Match(output ?? string.Empty, @"(\d+) files? pulled");
            return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
        }

        internal static string RemoteResultsFolder(string package)
        {
            if (string.IsNullOrWhiteSpace(package))
            {
                throw new ArgumentException("an application identifier is required", nameof(package));
            }

            return $"/sdcard/Android/data/{package}/files/{StoragePaths.Root}/{StoragePaths.Results}";
        }
    }
}
