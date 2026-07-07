using System;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Ide.Engines
{
    /// <summary>
    /// Contract for the integrated terminal backend.
    /// </summary>
    public interface ITerminalEngine : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Triggered when the terminal process produces standard output or error data.
        /// </summary>
        event Action<string>? OnOutputDataReceived;

        /// <summary>
        /// Starts the terminal process in the given working directory.
        /// </summary>
        Task StartAsync(string workingDirectory);

        /// <summary>
        /// Writes input to the terminal process.
        /// </summary>
        Task WriteAsync(string data);

        /// <summary>
        /// Resizes the pseudo-terminal (if supported).
        /// </summary>
        void Resize(int cols, int rows);
    }
}
