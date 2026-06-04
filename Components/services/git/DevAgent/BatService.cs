using System.Diagnostics;

namespace Syncro.Desktop.Services.DevAgent
{
    public class BatService
    {
        private readonly string _batDirectory = Path.Combine("wwwroot", "scripts", "generated");

        public BatService()
        {
            Directory.CreateDirectory(_batDirectory);
        }

        public string CreateBat(string fileName, string[] commands)
        {
            string filePath = Path.Combine(_batDirectory, $"{fileName}.bat");
            File.WriteAllLines(filePath, commands);
            return filePath;
        }

        public async Task<string> RunBatAsync(string filePath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            process.WaitForExit();
            return !string.IsNullOrWhiteSpace(error) ? error : output;
        }
    }
}
