namespace Syncro.CLI.Commands;

public static class InstallCommand
{
    public static void Run(string[] args)
    {
        bool uninstall = args.Contains("--uninstall");
        bool checkOnly = args.Contains("--check");

        string cliDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');

        if (checkOnly)
        {
            bool inPath = IsInPath(cliDir);
            Ui.Info(inPath
                ? $"syncro-cli IS registered in PATH → {cliDir}"
                : "syncro-cli is NOT in PATH. Run: syncro install");
            return;
        }

        if (!IsAdmin())
        {
            Ui.Warn("Administrator privileges required. Launching UAC elevation prompt...");
            try
            {
                ShellRunner.RunElevated(
                    Environment.ProcessPath ?? "syncro-cli.exe",
                    uninstall ? "install --uninstall" : "install");
            }
            catch (Exception ex) { Ui.Error($"Elevation failed: {ex.Message}"); }
            return;
        }

        try
        {
            const string varName = "PATH";
            var scope   = EnvironmentVariableTarget.Machine;
            string cur  = Environment.GetEnvironmentVariable(varName, scope) ?? "";
            var parts   = cur.Split(';').Select(p => p.Trim()).ToList();

            if (uninstall)
            {
                if (!parts.Contains(cliDir, StringComparer.OrdinalIgnoreCase))
                { Ui.Info("syncro-cli was not in PATH."); return; }

                string newPath = string.Join(";", parts.Where(p =>
                    !p.Equals(cliDir, StringComparison.OrdinalIgnoreCase)));
                Environment.SetEnvironmentVariable(varName, newPath, scope);
                Win32Api.BroadcastSettingsChange();
                Ui.Ok("Removed syncro-cli from System PATH.");
            }
            else
            {
                if (parts.Contains(cliDir, StringComparer.OrdinalIgnoreCase))
                { Ui.Ok("syncro-cli is already in System PATH."); return; }

                Environment.SetEnvironmentVariable(varName,
                    cur.TrimEnd(';') + ";" + cliDir, scope);
                Win32Api.BroadcastSettingsChange();
                Ui.Ok($"Added syncro-cli to System PATH → {cliDir}");
                Ui.Info("You can now run 'syncro' from any terminal without a full path.");
            }
        }
        catch (Exception ex) { Ui.Error($"PATH operation failed: {ex.Message}"); }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static bool IsAdmin()
    {
        var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static bool IsInPath(string dir)
    {
        string cur = Environment.GetEnvironmentVariable("PATH",
            EnvironmentVariableTarget.Machine) ?? "";
        return cur.Split(';').Any(p =>
            p.Trim().Equals(dir, StringComparison.OrdinalIgnoreCase));
    }
}
