using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net;

namespace DeveloperAI.BusinessLogic
{
    public class EnvironmentInfo
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsAvailable { get; set; } = false;
    }

    public class GitInfo
    {
        public bool IsGitInstalled { get; set; } = false;
        public string GitVersion { get; set; } = "";
        public string UserName { get; set; } = "";
        public string UserEmail { get; set; } = "";
    }

    public class EnvironmentManager
    {
        public GitInfo CurrentGitInfo { get; private set; } = new GitInfo();

        public async Task<EnvironmentInfo> CheckEnvironment(string environmentName)
        {
            var info = new EnvironmentInfo { Name = environmentName };

            try
            {
                switch (environmentName.ToLower())
                {
                    case "python":
                        info = await CheckPython();
                        break;
                    case "node.js":
                        info = await CheckNodeJS();
                        break;
                    case "java":
                        info = await CheckJava();
                        break;
                    case "c#/.net":
                        info = await CheckDotNet();
                        break;
                    case "go":
                        info = await CheckGo();
                        break;
                    case "rust":
                        info = await CheckRust();
                        break;
                    case "kotlin":
                        info = await CheckKotlin();
                        break;
                    case "dart":
                        info = await CheckDart();
                        break;
                    case "flutter":
                        info = await CheckFlutter();
                        break;
                    default:
                        info.IsAvailable = false;
                        break;
                }
            }
            catch (Exception ex)
            {
                info.IsAvailable = false;
                info.Version = $"Error: {ex.Message}";
            }

            return info;
        }

        private async Task<EnvironmentInfo> CheckKotlin()
        {
            var info = new EnvironmentInfo { Name = "Kotlin" };
            try
            {
                var result = await RunCommandAsync("kotlinc", "-version");
                if (result.Success || !string.IsNullOrEmpty(result.Error))
                {
                    info.IsAvailable = true;
                    info.Version = (result.Output + result.Error).Trim();
                    info.Path = await GetExecutablePath("kotlinc");
                }
            }
            catch { info.IsAvailable = false; }
            return info;
        }

        private async Task<EnvironmentInfo> CheckDart()
        {
            var info = new EnvironmentInfo { Name = "Dart" };
            try
            {
                var result = await RunCommandAsync("dart", "--version");
                if (result.Success || !string.IsNullOrEmpty(result.Error))
                {
                    info.IsAvailable = true;
                    var versionOutput = (result.Output + result.Error).Trim();
                    // Dart version is usually the first line
                    info.Version = versionOutput.Split('\n')[0].Trim();
                    info.Path = await GetExecutablePath("dart");
                }
            }
            catch { info.IsAvailable = false; }
            return info;
        }

        private async Task<EnvironmentInfo> CheckFlutter()
        {
            var info = new EnvironmentInfo { Name = "Flutter" };
            try
            {
                var result = await RunCommandAsync("flutter", "--version");
                if (result.Success || !string.IsNullOrEmpty(result.Error))
                {
                    info.IsAvailable = true;
                    var versionOutput = (result.Output + result.Error).Trim();
                    // Flutter version often contains a lot of info, take first line
                    info.Version = versionOutput.Split('\n')[0].Trim();
                    info.Path = await GetExecutablePath("flutter");
                }
            }
            catch { info.IsAvailable = false; }
            return info;
        }

        private async Task<EnvironmentInfo> CheckPython()
        {
            var info = new EnvironmentInfo { Name = "Python" };

            try
            {
                var result = await RunCommandAsync("python", "--version");
                if (result.Success)
                {
                    info.IsAvailable = true;
                    info.Version = result.Output.Trim();
                    info.Path = await GetExecutablePath("python");
                }
                else
                {
                    // Try python3
                    result = await RunCommandAsync("python3", "--version");
                    if (result.Success)
                    {
                        info.IsAvailable = true;
                        info.Version = result.Output.Trim();
                        info.Path = await GetExecutablePath("python3");
                    }
                }
            }
            catch
            {
                info.IsAvailable = false;
            }

            return info;
        }

        private async Task<EnvironmentInfo> CheckNodeJS()
        {
            var info = new EnvironmentInfo { Name = "Node.js" };

            try
            {
                var result = await RunCommandAsync("node", "--version");
                if (result.Success)
                {
                    info.IsAvailable = true;
                    info.Version = result.Output.Trim();
                    info.Path = await GetExecutablePath("node");
                }
            }
            catch
            {
                info.IsAvailable = false;
            }

            return info;
        }

        private async Task<EnvironmentInfo> CheckJava()
        {
            var info = new EnvironmentInfo { Name = "Java" };

            try
            {
                var result = await RunCommandAsync("java", "-version");
                if (result.Success)
                {
                    info.IsAvailable = true;
                    // Parse version from stderr output
                    var lines = result.Error.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("version"))
                        {
                            info.Version = line.Trim();
                            break;
                        }
                    }
                    info.Path = await GetExecutablePath("java");
                }
            }
            catch
            {
                info.IsAvailable = false;
            }

            return info;
        }

        private async Task<EnvironmentInfo> CheckDotNet()
        {
            var info = new EnvironmentInfo { Name = ".NET" };

            try
            {
                var result = await RunCommandAsync("dotnet", "--version");
                if (result.Success)
                {
                    info.IsAvailable = true;
                    info.Version = result.Output.Trim();
                    info.Path = await GetExecutablePath("dotnet");
                }
            }
            catch
            {
                info.IsAvailable = false;
            }

            return info;
        }

        private async Task<EnvironmentInfo> CheckGo()
        {
            var info = new EnvironmentInfo { Name = "Go" };

            try
            {
                var result = await RunCommandAsync("go", "version");
                if (result.Success)
                {
                    info.IsAvailable = true;
                    info.Version = result.Output.Trim();
                    info.Path = await GetExecutablePath("go");
                }
            }
            catch
            {
                info.IsAvailable = false;
            }

            return info;
        }

        private async Task<EnvironmentInfo> CheckRust()
        {
            var info = new EnvironmentInfo { Name = "Rust" };

            try
            {
                var result = await RunCommandAsync("cargo", "--version");
                if (result.Success)
                {
                    info.IsAvailable = true;
                    info.Version = result.Output.Trim();
                    info.Path = await GetExecutablePath("cargo");
                }
            }
            catch
            {
                info.IsAvailable = false;
            }

            return info;
        }

        private async Task<string> GetExecutablePath(string command)
        {
            try
            {
                var result = await RunCommandAsync("where", command);
                if (result.Success)
                {
                    var lines = result.Output.Split('\n');
                    return lines[0].Trim();
                }
            }
            catch
            {
                // Ignore errors
            }

            return "";
        }

        private async Task<(bool Success, string Output, string Error)> RunCommandAsync(string command, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                return (process.ExitCode == 0, output, error);
            }
            catch
            {
                return (false, "", "Command not found");
            }
        }

        public async Task<List<EnvironmentInfo>> CheckAllEnvironments()
        {
            var environments = new List<string> { "Python", "Node.js", "Java", "C#/.NET", "Go", "Rust", "Kotlin", "Dart", "Flutter" };
            var results = new List<EnvironmentInfo>();

            foreach (var env in environments)
            {
                var info = await CheckEnvironment(env);
                results.Add(info);
            }

            CurrentGitInfo = await CheckGit(); // Populate Git information

            return results;
        }

        public string GetEnvironmentSetupScript(string environmentName, string workingDirectory)
        {
            return environmentName.ToLower() switch
            {
                "python" => GeneratePythonSetupScript(workingDirectory),
                "node.js" => GenerateNodeJSSetupScript(workingDirectory),
                "java" => GenerateJavaSetupScript(workingDirectory),
                "c#/.net" => GenerateDotNetSetupScript(workingDirectory),
                "go" => GenerateGoSetupScript(workingDirectory),
                "rust" => GenerateRustSetupScript(workingDirectory),
                _ => "echo Unsupported environment: " + environmentName
            };
        }

        public async Task<GitInfo> CheckGit()
        {
            var gitInfo = new GitInfo();

            try
            {
                var versionResult = await RunCommandAsync("git", "--version");
                if (versionResult.Success)
                {
                    gitInfo.IsGitInstalled = true;
                    gitInfo.GitVersion = versionResult.Output.Trim();

                    var userNameResult = await RunCommandAsync("git", "config user.name");
                    if (userNameResult.Success)
                    {
                        gitInfo.UserName = userNameResult.Output.Trim();
                    }

                    var userEmailResult = await RunCommandAsync("git", "config user.email");
                    if (userEmailResult.Success)
                    {
                        gitInfo.UserEmail = userEmailResult.Output.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log or handle exception if necessary
                gitInfo.IsGitInstalled = false;
                gitInfo.GitVersion = $"Error: {ex.Message}";
            }

            return gitInfo;
        }

        private string GeneratePythonSetupScript(string workingDirectory)
        {
            return $@"@echo off
echo Setting up Python environment...
cd /d ""{workingDirectory}""

REM Check if Python is available
python --version >nul 2>&1
if errorlevel 1 (
    echo Error: Python not found. Please install Python.
    pause
    exit /b 1
)

REM Create virtual environment if it doesn't exist
if not exist venv (
    echo Creating virtual environment...
    python -m venv venv
)

REM Activate virtual environment
echo Activating virtual environment...
call venv\Scripts\activate.bat

echo ✅ Python environment ready!
echo Virtual environment: {workingDirectory}\venv
pause";
        }

        private string GenerateNodeJSSetupScript(string workingDirectory)
        {
            return $@"@echo off
echo Setting up Node.js environment...
cd /d ""{workingDirectory}""

REM Check if Node.js is available
node --version >nul 2>&1
if errorlevel 1 (
    echo Error: Node.js not found. Please install Node.js.
    pause
    exit /b 1
)

REM Initialize package.json if it doesn't exist
if not exist package.json (
    echo Initializing package.json...
    npm init -y
)

echo ✅ Node.js environment ready!
echo Project directory: {workingDirectory}
pause";
        }

        private string GenerateJavaSetupScript(string workingDirectory)
        {
            return $@"@echo off
echo Setting up Java environment...
cd /d ""{workingDirectory}""

REM Check if Java is available
java -version >nul 2>&1
if errorlevel 1 (
    echo Error: Java not found. Please install Java JDK.
    pause
    exit /b 1
)

REM Create Maven project structure
echo Creating Maven project structure...
mkdir src\main\java 2>nul
mkdir src\main\resources 2>nul
mkdir src\test\java 2>nul

echo ✅ Java environment ready!
echo Project directory: {workingDirectory}
pause";
        }

        private string GenerateDotNetSetupScript(string workingDirectory)
        {
            return $@"@echo off
echo Setting up .NET environment...
cd /d ""{workingDirectory}""

REM Check if .NET is available
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo Error: .NET SDK not found. Please install .NET SDK.
    pause
    exit /b 1
)

echo ✅ .NET environment ready!
echo Project directory: {workingDirectory}
pause";
        }

        private string GenerateGoSetupScript(string workingDirectory)
        {
            return $@"@echo off
echo Setting up Go environment...
cd /d ""{workingDirectory}""

REM Check if Go is available
go version >nul 2>&1
if errorlevel 1 (
    echo Error: Go not found. Please install Go.
    pause
    exit /b 1
)

REM Initialize Go module if go.mod doesn't exist
if not exist go.mod (
    echo Initializing Go module...
    go mod init myapp
)

echo ✅ Go environment ready!
echo Project directory: {workingDirectory}
pause";
        }

        private string GenerateRustSetupScript(string workingDirectory)
        {
            return $@"@echo off
echo Setting up Rust environment...
cd /d ""{workingDirectory}""

REM Check if Rust is available
cargo --version >nul 2>&1
if errorlevel 1 (
    echo Error: Rust not found. Please install Rust.
    pause
    exit /b 1
)

echo ✅ Rust environment ready!
echo Project directory: {workingDirectory}
pause";
        }

        public async Task<string> GetPythonVersion(string? workingDir = null)
        {
            var (stdout, stderr, exitCode) = await CommandRunner.RunCommand("python --version", workingDir ?? Directory.GetCurrentDirectory());
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                return stdout.Trim();
            if (!string.IsNullOrWhiteSpace(stderr))
                return stderr.Trim();
            return "Not Found";
        }

        public async Task<string> GetJavaVersion(string? workingDir = null)
        {
            var (stdout, stderr, exitCode) = await CommandRunner.RunCommand("java -version", workingDir ?? Directory.GetCurrentDirectory());
            if (!string.IsNullOrWhiteSpace(stderr))
                return stderr.Split('\n')[0].Trim();
            if (!string.IsNullOrWhiteSpace(stdout))
                return stdout.Trim();
            return "Not Found";
        }

        public async Task<string> GetNodeVersion(string? workingDir = null)
        {
            var (stdout, stderr, exitCode) = await CommandRunner.RunCommand("node --version", workingDir ?? Directory.GetCurrentDirectory());
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                return stdout.Trim();
            if (!string.IsNullOrWhiteSpace(stderr))
                return stderr.Trim();
            return "Not Found";
        }

        public bool IsPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] tcpEndPoints = ipProperties.GetActiveTcpListeners();
            foreach (var endPoint in tcpEndPoints)
            {
                if (endPoint.Port == port)
                    return true;
            }
            return false;
        }

        public async Task ExecuteSetupScript(string scriptContent, string workingDir)
        {
            try
            {
                var tempFile = Path.Combine(workingDir, "syncro_setup.bat");
                await File.WriteAllTextAsync(tempFile, scriptContent);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{tempFile}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing setup script: {ex.Message}");
            }
        }

        public Task<SystemResources> GetSystemResources()
        {
            var resources = new SystemResources();
            try
            {
                var drive = new DriveInfo("C");
                resources.AvailableDiskGB = Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024 * 1024), 1);
                resources.CpuCores = Environment.ProcessorCount;
                resources.TotalRamGB = 16.0; 
            }
            catch { }
            return Task.FromResult(resources);
        }
    }

    public class SystemResources
    {
        public double AvailableDiskGB { get; set; }
        public int CpuCores { get; set; }
        public double TotalRamGB { get; set; }
    }
}
 