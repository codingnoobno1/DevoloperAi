namespace Syncro.Desktop.Services.SyncroCLI.Core.Platform
{
    public interface IPlatformService
    {
        PlatformType Type { get; }
        string Shell { get; }
        string ScriptExtension { get; }
        string ElevationCommand { get; }
        string WrapCommand(string command);
    }
}
