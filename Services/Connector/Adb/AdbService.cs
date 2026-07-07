using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Connector.Adb
{
    public sealed record AdbDevice(
        string Id,
        string Type,
        string Model,
        bool IsAuthorized,
        string? AndroidVersion = null);

    public static class AdbService
    {
        // ── Device enumeration ────────────────────────────────────────────────

        public static async Task<List<AdbDevice>> GetDevicesAsync()
        {
            var output = await RunToolAsync("adb", "devices -l");
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("*"))
                .Select(ParseDevice)
                .OfType<AdbDevice>()
                .ToList();
        }

        private static AdbDevice? ParseDevice(string line)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            var id   = parts[0];
            var type = parts[1];
            var auth = type == "device";
            var model = parts.FirstOrDefault(p => p.StartsWith("model:"))?.Replace("model:", "")
                     ?? parts.FirstOrDefault(p => p.StartsWith("product:"))?.Replace("product:", "")
                     ?? id;
            return new AdbDevice(id, type, model, auth);
        }

        // ── Flutter devices ───────────────────────────────────────────────────

        public static async Task<List<string>> GetFlutterDevicesAsync()
        {
            var output = await RunToolAsync("flutter", "devices");
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(l => l.Contains("•") || l.Contains("android") || l.Contains("ios"))
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
        }

        // ── Tool availability ─────────────────────────────────────────────────

        public static bool IsAdbAvailable()    => CheckTool("adb",     "version");
        public static bool IsScrcpyAvailable() => CheckTool("scrcpy",  "--version");
        public static bool IsFlutterAvailable() => CheckTool("flutter", "--version");

        private static bool CheckTool(string tool, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(tool, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                });
                return p != null && p.WaitForExit(3000) && p.ExitCode == 0;
            }
            catch { return false; }
        }

        // ── Actions ───────────────────────────────────────────────────────────

        public static void LaunchScrcpy(AdbDevice device, bool stayAwake = true)
        {
            var args = $"--serial {device.Id} --window-title \"{device.Model}\" --stay-awake --turn-screen-on";
            try { Process.Start(new ProcessStartInfo("scrcpy", args) { UseShellExecute = true }); }
            catch { }
        }

        public static void OpenAdbShell(AdbDevice device)
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/k adb -s {device.Id} shell")
                    { UseShellExecute = true });
            }
            catch { }
        }

        public static async Task<string> GetAndroidVersionAsync(AdbDevice device)
        {
            return (await RunToolAsync("adb", $"-s {device.Id} shell getprop ro.build.version.release")).Trim();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static async Task<string> RunToolAsync(string tool, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(tool, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                return output;
            }
            catch { return ""; }
        }
    }
}
