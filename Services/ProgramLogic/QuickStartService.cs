using System.Threading.Tasks;
using System.IO;
using System.Reflection.Metadata;
using Syncro.Desktop.Services.ProgramLogic;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class QuickStartService
    {
        private readonly CommandExecutionService _commandExecutionService;
        private readonly FolderCreationService _folderCreationService; // Inject FolderCreationService

        public QuickStartService(CommandExecutionService commandExecutionService, FolderCreationService folderCreationService)
        {
            _commandExecutionService = commandExecutionService;
            _folderCreationService = folderCreationService;
        }

        public async Task<(bool success, string message)> CreatePythonVenvAndFlask(string workingDirectory)
        {
            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(workingDirectory);
            if (!folderSuccess)
            {
                return (false, $"Failed to prepare workspace: {folderMessage}");
            }

            string scriptContent = $@"@echo off
REM Python Virtual Environment and Flask Setup
cd /d ""{workingDirectory}""

python -m venv venv
call venv\Scripts\activate.bat
pip install Flask
echo Flask project setup complete.
";
            return await ExecuteScriptContent(scriptContent, workingDirectory, "python_flask_setup.bat");
        }

        public async Task<(bool success, string message)> CreateJavaMavenProject(string groupId, string artifactId, string workingDirectory)
        {
            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(workingDirectory);
            if (!folderSuccess)
            {
                return (false, $"Failed to prepare workspace: {folderMessage}");
            }

            string scriptContent = $@"@echo off
REM Java Maven Project Setup
cd /d ""{workingDirectory}""

mvn archetype:generate -DgroupId={groupId} -DartifactId={artifactId} -DarchetypeArtifactId=maven-archetype-quickstart -DinteractiveMode=false
echo Maven project created.
";
            return await ExecuteScriptContent(scriptContent, workingDirectory, "java_maven_setup.bat");
        }

        public async Task<(bool success, string message)> InstallChoco(string workingDirectory)
        {
            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(workingDirectory);
            if (!folderSuccess)
            {
                return (false, $"Failed to prepare workspace: {folderMessage}");
            }

            string scriptContent = $@"@echo off
REM Install Chocolatey (Requires Administrative PowerShell)
SET ""TEMPDIR""=%TEMP%
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -InputFormat None -ExecutionPolicy Bypass -Command ""iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))"" && SET ""TEMP""=%TEMPDIR%

echo Chocolatey installed. You may need to restart your terminal.
";
            return await ExecuteScriptContent(scriptContent, workingDirectory, "install_choco.bat");
        }

        public async Task<(bool success, string message)> InstallScoop(string workingDirectory)
        {
            var (folderSuccess, folderMessage) = await _folderCreationService.CreateFolder(workingDirectory);
            if (!folderSuccess)
            {
                return (false, $"Failed to prepare workspace: {folderMessage}");
            }

            string scriptContent = $@"@echo off
REM Install Scoop (Requires PowerShell)
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
irm get.scoop.sh | iex

echo Scoop installed. You may need to restart your terminal.
";
            return await ExecuteScriptContent(scriptContent, workingDirectory, "install_scoop.bat");
        }

        private async Task<(bool success, string message)> ExecuteScriptContent(string scriptContent, string workingDirectory, string fileName)
        {
            string scriptFilePath = Path.Combine(workingDirectory, fileName);
            try
            {
                await File.WriteAllTextAsync(scriptFilePath, scriptContent);
                var (execSuccess, execOutput, execError) = await _commandExecutionService.ExecuteCommand(scriptFilePath, workingDirectory, runInShell: true);

                if (execSuccess)
                {
                    return (true, $"Script executed successfully.\nOutput:\n{execOutput}");
                }
                else
                {
                    return (false, $"Script execution failed with exit code.\nOutput:\n{execOutput}\nError:\n{execError}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error during script execution: {ex.Message}");
            }
        }
    }
}
