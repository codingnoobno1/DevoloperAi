using System;
using System.Runtime.InteropServices;

namespace Syncro.Desktop.Services.SyncroCLI.Core.Platform
{
    public enum PlatformType
    {
        Windows,
        Linux,
        Mac,
        WSL
    }

    public static class PlatformDetector
    {
        public static PlatformType Detect()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") != null)
                    return PlatformType.WSL;

                return PlatformType.Windows;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return PlatformType.Linux;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return PlatformType.Mac;

            throw new PlatformNotSupportedException("Unknown or unsupported platform");
        }
    }
}
