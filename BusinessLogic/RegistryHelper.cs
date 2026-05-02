using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace DeveloperAI.BusinessLogic
{
    public static class RegistryHelper
    {
        // Returns a list of installed software display names
        public static List<string> GetInstalledSoftware()
        {
            var result = new List<string>();
            string[] registryKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            // Check both HKLM and HKCU
            foreach (var root in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                using (var baseKey = RegistryKey.OpenBaseKey(root, RegistryView.Registry64))
                {
                    foreach (var keyPath in registryKeys)
                    {
                        using (var key = baseKey.OpenSubKey(keyPath))
                        {
                            if (key == null) continue;
                            foreach (var subkeyName in key.GetSubKeyNames())
                            {
                                using (var subkey = key.OpenSubKey(subkeyName))
                                {
                                    var displayName = subkey?.GetValue("DisplayName") as string;
                                    if (!string.IsNullOrEmpty(displayName))
                                        result.Add(displayName);
                                }
                            }
                        }
                    }
                }
            }
            return result;
        }

        // Reads a specific Windows setting from the registry
        public static string GetWindowsSetting(string keyPath, string valueName, RegistryHive hive = RegistryHive.LocalMachine)
        {
            using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (var key = baseKey.OpenSubKey(keyPath))
            {
                if (key == null) return null;
                var value = key.GetValue(valueName);
                return value?.ToString();
            }
        }
    }
} 