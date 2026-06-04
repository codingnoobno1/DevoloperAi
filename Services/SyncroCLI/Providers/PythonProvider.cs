using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.SyncroCLI.Execution;

namespace Syncro.Desktop.Services.SyncroCLI.Providers
{
    public class PythonProvider : IProjectProvider
    {
        private readonly ProcessRunner _runner;

        public PythonProvider(ProcessRunner runner)
        {
            _runner = runner;
        }

        public string StackName => "python";

        public async Task<bool> Create(string name, string targetPath)
        {
            try
            {
                string projectDir = Path.Combine(targetPath, name);
                if (!Directory.Exists(projectDir)) Directory.CreateDirectory(projectDir);

                await _runner.Run("python -m venv venv", projectDir);
                await _runner.Run("venv\\Scripts\\pip install fastapi uvicorn", projectDir);

                await File.WriteAllTextAsync(Path.Combine(projectDir, "main.py"), @"
from fastapi import FastAPI
app = FastAPI()

@app.get('/')
def read_root():
    return {""Hello"": ""World""}
");
                await File.WriteAllTextAsync(Path.Combine(projectDir, "requirements.txt"), "fastapi\nuvicorn");
                return true;
            }
            catch { return false; }
        }
    }
}
