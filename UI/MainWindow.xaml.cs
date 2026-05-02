using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using System.Net.Sockets;
using Microsoft.Win32;
using System.Windows.Forms;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using DeveloperAI.BusinessLogic;

namespace DeveloperAI
{
    public partial class MainWindow : Window
    {
        private string currentScriptPath = "";
        private string currentWorkingDirectory = "";

        public MainWindow()
        {
            InitializeComponent();
            currentWorkingDirectory = WorkingDirectoryInput.Text;
            // Ensure env folders exist on startup
            EnvFolderManager.EnsureEnvFolders(currentWorkingDirectory);
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            string prompt = PromptInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                System.Windows.MessageBox.Show("Please enter a prompt.", "Input Needed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResponseDisplay.Text = "Generating advanced script using AI...\n";

            // Build enhanced prompt with context
            string enhancedPrompt = BuildEnhancedPrompt(prompt);
            string? response = await AIClient.CallLLM(enhancedPrompt);

            if (!string.IsNullOrEmpty(response))
            {
                // Validate that the response is a proper batch script
                var cleanedResponse = ValidateAndCleanBatchScript(response);
                
                if (string.IsNullOrEmpty(cleanedResponse))
                {
                    ResponseDisplay.Text = "❌ Failed to generate valid batch script. Please try again.";
                    return;
                }

                ResponseDisplay.Text = $"--- Generated Advanced Script ---\n{cleanedResponse}";

                currentScriptPath = Path.Combine(currentWorkingDirectory, "generated_script.bat");
                File.WriteAllText(currentScriptPath, cleanedResponse);

                // Save project metadata
                SaveProjectMetadata();

                ResponseDisplay.Text += $"\n✅ Script saved to: {currentScriptPath}";
            }
            else
            {
                ResponseDisplay.Text = "❌ Failed to generate script.";
            }
        }

        private string BuildEnhancedPrompt(string userPrompt)
        {
            string environment = (EnvironmentComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "None";
            string port = PortInput.Text.Trim();
            string workingDir = WorkingDirectoryInput.Text.Trim();

            return $@"Context:
- Working Directory: {workingDir}
- Environment: {environment}
- Port: {port}

User Request: {userPrompt}

Generate a comprehensive batch script that:
1. Changes to the specified working directory
2. Activates the appropriate environment if specified
3. Handles port configuration
4. Executes the user's request
5. Includes proper error handling and status messages

Make the script robust and user-friendly.";
        }

        private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select Working Directory";
                dialog.SelectedPath = WorkingDirectoryInput.Text;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    WorkingDirectoryInput.Text = dialog.SelectedPath;
                    currentWorkingDirectory = dialog.SelectedPath;
                }
            }
        }

        private async void CheckPort_Click(object sender, RoutedEventArgs e)
        {
            string portText = PortInput.Text.Trim();
            if (!int.TryParse(portText, out int port))
            {
                System.Windows.MessageBox.Show("Please enter a valid port number.", "Invalid Port", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResponseDisplay.Text = $"🔍 Checking if port {port} is available...\n";

            bool isAvailable = !EnvironmentManager.IsPortInUse(port);
            
            if (isAvailable)
            {
                ResponseDisplay.Text += $"✅ Port {port} is available and ready to use.\n";
            }
            else
            {
                ResponseDisplay.Text += $"❌ Port {port} is already in use. Please choose a different port.\n";
            }
        }

        private async void InstallLibraries_Click(object sender, RoutedEventArgs e)
        {
            string environment = (EnvironmentComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "None";
            
            if (environment == "None")
            {
                System.Windows.MessageBox.Show("Please select an environment first.", "No Environment Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ResponseDisplay.Text = $"📦 Checking {environment} environment...\n";

            // Check environment availability first
            var envInfo = await EnvironmentManager.CheckEnvironment(environment);
            
            if (!envInfo.IsAvailable)
            {
                ResponseDisplay.Text += $"❌ {environment} is not available on this system.\n";
                ResponseDisplay.Text += $"Please install {environment} and try again.\n";
                return;
            }

            ResponseDisplay.Text += $"✅ {environment} found: {envInfo.Version}\n";
            ResponseDisplay.Text += $"📦 Installing libraries for {environment}...\n";

            string installScript = GenerateInstallScript(environment);
            
            if (!string.IsNullOrEmpty(installScript))
            {
                string installPath = Path.Combine(currentWorkingDirectory, ".scripts", "install_libraries.bat");
                Directory.CreateDirectory(Path.GetDirectoryName(installPath));
                File.WriteAllText(installPath, installScript);

                ResponseDisplay.Text += $"✅ Installation script created: {installPath}\n";
                ResponseDisplay.Text += "📋 Script content:\n" + installScript;

                if (System.Windows.MessageBox.Show("Do you want to run the installation script now?", "Install Libraries", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    try
                    {
                        var (stdout, stderr, exitCode) = await ScriptManager.RunScript("install_libraries.bat", currentWorkingDirectory, true);
                        ResponseDisplay.Text += $"📋 Installation Output:\n{stdout}\n";
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            ResponseDisplay.Text += $"⚠️ Errors:\n{stderr}\n";
                        }
                        ResponseDisplay.Text += $"✅ Installation completed with exit code: {exitCode}\n";
                    }
                    catch (Exception ex)
                    {
                        ResponseDisplay.Text += $"❌ Failed to run installation script: {ex.Message}\n";
                    }
                }
            }
        }

        private string GenerateInstallScript(string environment)
        {
            return environment.ToLower() switch
            {
                "python" => @"@echo off
echo Installing Python libraries...
echo.

REM Check if pip is available
python -m pip --version >nul 2>&1
if errorlevel 1 (
    echo Error: pip not found. Please install Python with pip.
    pause
    exit /b 1
)

REM Install common Python libraries
echo Installing Flask...
python -m pip install flask

echo Installing requests...
python -m pip install requests

echo Installing numpy...
python -m pip install numpy

echo Installing pandas...
python -m pip install pandas

echo Installing matplotlib...
python -m pip install matplotlib

echo.
echo ✅ Python libraries installed successfully!
pause",
                
                "node.js" => @"@echo off
echo Installing Node.js packages...
echo.

REM Check if npm is available
npm --version >nul 2>&1
if errorlevel 1 (
    echo Error: npm not found. Please install Node.js.
    pause
    exit /b 1
)

REM Install common Node.js packages
echo Installing Express...
npm install express

echo Installing axios...
npm install axios

echo Installing nodemon...
npm install -g nodemon

echo Installing cors...
npm install cors

echo.
echo ✅ Node.js packages installed successfully!
pause",
                
                "java" => @"@echo off
echo Setting up Java project...
echo.

REM Check if Java is available
java -version >nul 2>&1
if errorlevel 1 (
    echo Error: Java not found. Please install Java JDK.
    pause
    exit /b 1
)

REM Create Maven project structure
echo Creating Maven project structure...
mkdir src\main\java
mkdir src\main\resources
mkdir src\test\java

REM Create pom.xml if it doesn't exist
if not exist pom.xml (
    echo Creating pom.xml...
    echo ^<?xml version=""1.0"" encoding=""UTF-8""?^> > pom.xml
    echo ^<project xmlns=""http://maven.apache.org/POM/4.0.0""^> >> pom.xml
    echo     ^<modelVersion^>4.0.0^</modelVersion^> >> pom.xml
    echo     ^<groupId^>com.example^</groupId^> >> pom.xml
    echo     ^<artifactId^>my-app^</artifactId^> >> pom.xml
    echo     ^<version^>1.0-SNAPSHOT^</version^> >> pom.xml
    echo ^</project^> >> pom.xml
)

echo.
echo ✅ Java project structure created!
pause",
                
                "c#/.net" => @"@echo off
echo Setting up .NET project...
echo.

REM Check if dotnet is available
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo Error: .NET SDK not found. Please install .NET SDK.
    pause
    exit /b 1
)

REM Create new .NET project
echo Creating new .NET project...
dotnet new webapi -n MyWebApi

echo.
echo ✅ .NET project created successfully!
pause",
                
                "go" => @"@echo off
echo Setting up Go project...
echo.

REM Check if Go is available
go version >nul 2>&1
if errorlevel 1 (
    echo Error: Go not found. Please install Go.
    pause
    exit /b 1
)

REM Initialize Go module
echo Initializing Go module...
go mod init myapp

REM Install common Go packages
echo Installing Gin web framework...
go get github.com/gin-gonic/gin

echo Installing Gorilla Mux...
go get github.com/gorilla/mux

echo.
echo ✅ Go project initialized successfully!
pause",
                
                "rust" => @"@echo off
echo Setting up Rust project...
echo.

REM Check if Cargo is available
cargo --version >nul 2>&1
if errorlevel 1 (
    echo Error: Rust/Cargo not found. Please install Rust.
    pause
    exit /b 1
)

REM Create new Rust project
echo Creating new Rust project...
cargo new myapp

echo.
echo ✅ Rust project created successfully!
pause",
                
                _ => "echo Unsupported environment: " + environment
            };
        }

        private void OpenTerminal_Click(object sender, RoutedEventArgs e)
        {
            string workingDir = WorkingDirectoryInput.Text.Trim();
            
            if (!Directory.Exists(workingDir))
            {
                System.Windows.MessageBox.Show("Working directory does not exist. Please select a valid directory.", "Invalid Directory", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d \"{workingDir}\"",
                    WorkingDirectory = workingDir,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                ResponseDisplay.Text += $"💻 Terminal opened in: {workingDir}\n";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open terminal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RunScript_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentScriptPath) || !File.Exists(currentScriptPath))
            {
                System.Windows.MessageBox.Show("No script available to run. Please generate a script first.", "No Script", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ResponseDisplay.Text += $"\n▶️ Running script: {currentScriptPath}\n";
            
            try
            {
                // Save the generated script to .scripts directory
                string scriptName = Path.GetFileName(currentScriptPath);
                string scriptsDir = Path.Combine(currentWorkingDirectory, ".scripts");
                Directory.CreateDirectory(scriptsDir);
                string scriptPathInScripts = Path.Combine(scriptsDir, scriptName);
                File.Copy(currentScriptPath, scriptPathInScripts, true);

                var (stdout, stderr, exitCode) = await ScriptManager.RunScript(scriptName, currentWorkingDirectory, true);
                
                ResponseDisplay.Text += $"📋 Script Output:\n{stdout}\n";
                if (!string.IsNullOrEmpty(stderr))
                {
                    ResponseDisplay.Text += $"⚠️ Errors:\n{stderr}\n";
                }
                
                ResponseDisplay.Text += $"✅ Script completed with exit code: {exitCode}\n";
            }
            catch (Exception ex)
            {
                ResponseDisplay.Text += $"❌ Failed to run script: {ex.Message}\n";
            }
        }

        private async void CheckEnvironments_Click(object sender, RoutedEventArgs e)
        {
            ResponseDisplay.Text = "🔍 Checking all development environments...\n\n";
            
            // Use new version detection methods
            var pythonVersion = await EnvironmentManager.GetPythonVersion(currentWorkingDirectory);
            var javaVersion = await EnvironmentManager.GetJavaVersion(currentWorkingDirectory);
            var nodeVersion = await EnvironmentManager.GetNodeVersion(currentWorkingDirectory);
            
            ResponseDisplay.Text += $"🐍 Python: {pythonVersion}\n";
            ResponseDisplay.Text += $"☕ Java: {javaVersion}\n";
            ResponseDisplay.Text += $"🟢 Node.js: {nodeVersion}\n\n";
            
            // Also check all environments using existing method
            var environments = await EnvironmentManager.CheckAllEnvironments();
            
            ResponseDisplay.Text += "📋 Detailed Environment Status:\n";
            foreach (var env in environments)
            {
                if (env.IsAvailable)
                {
                    ResponseDisplay.Text += $"✅ {env.Name}: {env.Version}\n";
                    if (!string.IsNullOrEmpty(env.Path))
                    {
                        ResponseDisplay.Text += $"   📁 Path: {env.Path}\n";
                    }
                }
                else
                {
                    ResponseDisplay.Text += $"❌ {env.Name}: Not installed\n";
                }
                ResponseDisplay.Text += "\n";
            }
            
            ResponseDisplay.Text += "💡 Tip: Use 'Install Libraries' button to set up your selected environment.\n";
        }

        private string ValidateAndCleanBatchScript(string response)
        {
            if (string.IsNullOrEmpty(response))
                return "";

            var lines = response.Split('\n');
            var cleanedLines = new List<string>();
            bool foundScriptStart = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip explanatory text at the beginning
                if (!foundScriptStart)
                {
                    // Check if this line looks like a batch command
                    if (trimmedLine.StartsWith("@echo") || 
                        trimmedLine.StartsWith("echo") ||
                        trimmedLine.StartsWith("REM") ||
                        trimmedLine.StartsWith("::") ||
                        trimmedLine.StartsWith("cd") ||
                        trimmedLine.StartsWith("if") ||
                        trimmedLine.StartsWith("for") ||
                        trimmedLine.StartsWith("set") ||
                        trimmedLine.StartsWith("call") ||
                        trimmedLine.StartsWith("python") ||
                        trimmedLine.StartsWith("node") ||
                        trimmedLine.StartsWith("java") ||
                        trimmedLine.StartsWith("dotnet") ||
                        trimmedLine.StartsWith("go") ||
                        trimmedLine.StartsWith("cargo") ||
                        trimmedLine.StartsWith("npm") ||
                        trimmedLine.StartsWith("pip") ||
                        trimmedLine.StartsWith("mvn"))
                    {
                        foundScriptStart = true;
                    }
                    else if (trimmedLine.Contains("script") || 
                             trimmedLine.Contains("here") || 
                             trimmedLine.Contains("generated") ||
                             trimmedLine.StartsWith("```") ||
                             trimmedLine.StartsWith("`") ||
                             trimmedLine.StartsWith("#") ||
                             trimmedLine.StartsWith("*") ||
                             trimmedLine.StartsWith("---"))
                    {
                        continue; // Skip explanatory lines
                    }
                }

                if (foundScriptStart)
                {
                    cleanedLines.Add(line);
                }
            }

            var cleanedScript = string.Join("\n", cleanedLines).Trim();
            
            // Ensure the script starts with @echo off if it doesn't already
            if (!cleanedScript.StartsWith("@echo") && !cleanedScript.StartsWith("echo"))
            {
                cleanedScript = "@echo off\n" + cleanedScript;
            }

            return cleanedScript;
        }

        private void SaveProjectMetadata()
        {
            try
            {
                string environment = (EnvironmentComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "None";
                string port = PortInput.Text.Trim();
                
                var metadata = new ProjectMetadata
                {
                    Language = environment,
                    Env = environment,
                    Dependencies = new string[] { "Generated by DeveloperAI" },
                    PythonVersion = "Will be detected on next check"
                };

                MetadataManager.SaveMetadata(metadata, currentWorkingDirectory);
                ResponseDisplay.Text += $"\n📋 Project metadata saved to .projectmeta/metadata.json";
            }
            catch (Exception ex)
            {
                ResponseDisplay.Text += $"\n⚠️ Failed to save project metadata: {ex.Message}";
            }
        }

        private void OpenEnvTerminal_Click(object sender, RoutedEventArgs e)
        {
            string selectedLang = (EnvTerminalComboBox.SelectedItem as ComboBoxItem)?.Content.ToString();
            if (string.IsNullOrEmpty(selectedLang))
            {
                ResponseDisplay.Text += "\n❌ Please select an environment.";
                return;
            }

            EnvFolderManager.EnsureEnvFolders(currentWorkingDirectory);
            string envDir = EnvFolderManager.GetEnvFolder(currentWorkingDirectory, selectedLang);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = envDir,
                UseShellExecute = false
            };

            // Set environment variables for the selected language
            switch (selectedLang.ToLower())
            {
                case "python":
                    psi.EnvironmentVariables["PYTHONPATH"] = envDir;
                    break;
                case "node":
                    psi.EnvironmentVariables["NODE_PATH"] = envDir;
                    break;
                case "java":
                    psi.EnvironmentVariables["CLASSPATH"] = Path.Combine(envDir, "libs");
                    break;
                // Add more as needed
            }

            try
            {
                Process.Start(psi);
                ResponseDisplay.Text += $"\n🖥️ Opened terminal for {selectedLang} environment at {envDir}";
            }
            catch (Exception ex)
            {
                ResponseDisplay.Text += $"\n❌ Failed to open terminal: {ex.Message}";
            }
        }
    }
}
