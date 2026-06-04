namespace Syncro.Desktop.Services.SyncroCLI.Core.Platform
{
    public class WindowsPlatform : IPlatformService
    {
        public PlatformType Type => PlatformType.Windows;
        public string Shell => "powershell.exe";
        public string ScriptExtension => ".ps1";
        public string ElevationCommand => "Start-Process powershell -Verb runAs";

        public string WrapCommand(string command)
            => $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
    }

    public class LinuxPlatform : IPlatformService
    {
        public virtual PlatformType Type => PlatformType.Linux;
        public virtual string Shell => "/bin/bash";
        public virtual string ScriptExtension => ".sh";
        public virtual string ElevationCommand => "sudo";

        public virtual string WrapCommand(string command)
            => $"-c \"{command}\"";
    }

    public class MacPlatform : LinuxPlatform
    {
        public override PlatformType Type => PlatformType.Mac;
        public override string Shell => "/bin/zsh";
    }

    public class WslPlatform : LinuxPlatform
    {
        public override PlatformType Type => PlatformType.WSL;
        // WSL uses bash inside Windows
    }
}
