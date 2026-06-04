using System;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class FolderCreationService
    {
        public Task<(bool success, string message)> CreateFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult((false, "Path cannot be empty."));
            }

            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                    return Task.FromResult((true, $"Directory created: {path}"));
                }
                else
                {
                    return Task.FromResult((true, $"Directory already exists: {path}"));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult((false, $"Failed to create directory {path}: {ex.Message}"));
            }
        }
    }
}
