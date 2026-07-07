using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.AST.Git;

public class GitRepoService
{
    public string ClonesRootDirectory
    {
        get
        {
            string workspaceRoot = Microsoft.Maui.Storage.Preferences.Default.Get("__syncro_workspace_root__", "");
            if (string.IsNullOrWhiteSpace(workspaceRoot))
            {
                workspaceRoot = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "SyncroWorkspace");
            }
            string path = Path.Combine(workspaceRoot, "cloned_repos");
            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch { }
            return path;
        }
    }

    public GitRepoService()
    {
    }

    public long GetClonesSize()
    {
        try
        {
            string path = ClonesRootDirectory;
            if (!Directory.Exists(path)) return 0;
            return GetDirectorySize(path);
        }
        catch
        {
            return 0;
        }
    }

    private long GetDirectorySize(string folderPath)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch { }
            }
        }
        catch { }
        return size;
    }

    public async Task<string> CloneRepositoryAsync(string gitUrl, string projectName, Action<string>? onProgressLog = null)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            throw new ArgumentException("Git URL cannot be empty.", nameof(gitUrl));
        }

        string folderName = string.IsNullOrWhiteSpace(projectName) 
            ? GetFolderNameFromUrl(gitUrl) 
            : projectName.Replace(" ", "_");

        string targetPath = Path.Combine(ClonesRootDirectory, folderName);

        // Deduplicate folder name if already exists
        int index = 1;
        while (Directory.Exists(targetPath))
        {
            targetPath = Path.Combine(ClonesRootDirectory, $"{folderName}_{index}");
            index++;
        }

        onProgressLog?.Invoke($"Initializing clone of: {gitUrl}");
        onProgressLog?.Invoke($"Destination folder: {targetPath}");

        var tcs = new TaskCompletionSource<bool>();

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"clone \"{gitUrl}\" \"{targetPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                onProgressLog?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                onProgressLog?.Invoke(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new Exception($"git clone process exited with code {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            onProgressLog?.Invoke($"[ERROR] Git clone failed: {ex.Message}");
            throw;
        }

        onProgressLog?.Invoke($"[SUCCESS] Git clone completed. Path: {targetPath}");
        return targetPath;
    }

    private string GetFolderNameFromUrl(string gitUrl)
    {
        try
        {
            string lastPart = gitUrl.Trim().TrimEnd('/');
            int lastSlash = lastPart.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                lastPart = lastPart.Substring(lastSlash + 1);
            }

            if (lastPart.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                lastPart = lastPart.Substring(0, lastPart.Length - 4);
            }

            return lastPart;
        }
        catch
        {
            return "cloned_project";
        }
    }
}
