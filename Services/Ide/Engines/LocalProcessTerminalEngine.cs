using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Ide.Engines
{
    public class LocalProcessTerminalEngine : ITerminalEngine
    {
        private Process? _process;
        private StreamWriter? _stdin;
        public event Action<string>? OnOutputDataReceived;

        public Task StartAsync(string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            _process = new Process { StartInfo = psi };

            _process.OutputDataReceived += (s, e) => {
                if (e.Data != null) OnOutputDataReceived?.Invoke(e.Data);
            };
            _process.ErrorDataReceived += (s, e) => {
                if (e.Data != null) OnOutputDataReceived?.Invoke(e.Data);
            };

            _process.Start();
            _stdin = _process.StandardInput;
            // cmd.exe doesn't automatically emit a prompt via stdout when redirected,
            // so the JS layer currently manually emits a prompt.

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            return Task.CompletedTask;
        }

        public async Task WriteAsync(string data)
        {
            if (_stdin != null)
            {
                await _stdin.WriteLineAsync(data);
                await _stdin.FlushAsync();
            }
        }

        public void Resize(int cols, int rows)
        {
            // Not supported without true ConPTY
        }

        public void Dispose()
        {
            if (_process != null)
            {
                try { _process.Kill(); } catch { }
                _process.Dispose();
                _process = null;
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
