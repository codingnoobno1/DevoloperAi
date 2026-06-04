namespace Syncro.Desktop.Services.ProgramLogic
{
    public interface IProjectGenerator
    {
        Task<(bool success, string message)> GenerateProject(string userPrompt, string targetWorkspacePath);
    }
}
