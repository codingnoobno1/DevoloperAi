using Syncro.Desktop.Services.SyncroCLI.Core.Platform;

namespace Syncro.Desktop.Services.SyncroCLI.Execution
{
    public class ElevationService
    {
        private readonly IPlatformService _platform;

        public ElevationService(IPlatformService platform)
        {
            _platform = platform;
        }

        public string Elevate(string command)
        {
            if (_platform.Type == PlatformType.Windows)
            {
                // In Windows, we often need to wrap the command in a Start-Process with runAs verb
                // for the actual shell, but since ProcessRunner uses PSI, 
                // we might need to handle this at the ProcessRunner level if PSI Verb is used.
                // For now, return the elevation command prefix if applicable.
                return $"{_platform.ElevationCommand} {command}";
            }

            return $"{_platform.ElevationCommand} {command}";
        }

        public bool IsWindows() => _platform.Type == PlatformType.Windows;
    }
}
