using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>Detected credential/signing capabilities for a machine + repo.</summary>
    public sealed class GitCredentialStatus
    {
        public string? SshKeyPath { get; set; }
        public bool SshAgentRunning { get; set; }
        public string? GpgKeyId { get; set; }
        public bool SigningEnabled { get; set; }      // repo has commit.gpgsign=true + a signing key
        public string? CredentialHelper { get; set; } // for HTTPS/PAT
        public string Summary =>
            $"SSH key {(SshKeyPath != null ? "✓" : "✗")} · agent {(SshAgentRunning ? "✓" : "✗")} · " +
            $"GPG {(GpgKeyId != null ? "✓" : "✗")}{(SigningEnabled ? " (signing on)" : "")} · " +
            $"PAT helper {(CredentialHelper != null ? CredentialHelper : "✗")}";
    }

    /// <summary>
    /// Detects and wires git credentials through git's OWN native mechanisms — never storing a raw
    /// key, token or passphrase (git-sync-engine.md §4). Detection is read-only; wiring uses
    /// <c>git config</c> so plain fetch/commit/push then authenticate automatically:
    /// <list type="bullet">
    ///   <item>SSH → <c>core.sshCommand = ssh -i &lt;key&gt;</c> (or a running ssh-agent)</item>
    ///   <item>PAT → the OS git credential helper (Windows Credential Manager)</item>
    ///   <item>GPG → <c>commit.gpgsign=true</c> + <c>user.signingkey</c>; git invokes gpg-agent</item>
    /// </list>
    /// </summary>
    public sealed class GitCredentialResolver
    {
        private readonly IGitEngine _git;
        public GitCredentialResolver(IGitEngine git) => _git = git;

        public async Task<GitCredentialStatus> DetectAsync(string? repo = null, CancellationToken ct = default)
        {
            var s = new GitCredentialStatus();

            // SSH key on disk (common names).
            var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
            foreach (var name in new[] { "id_ed25519", "id_rsa", "id_ecdsa" })
            {
                var p = Path.Combine(sshDir, name);
                if (File.Exists(p)) { s.SshKeyPath = p; break; }
            }

            // ssh-agent: SSH_AUTH_SOCK (posix) or a successful `ssh-add -l`.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_AUTH_SOCK")))
                s.SshAgentRunning = true;
            else
            {
                var (ok, outp, _) = await RunAsync("ssh-add", "-l", null, ct);
                s.SshAgentRunning = ok && !outp.Contains("no identities", StringComparison.OrdinalIgnoreCase);
            }

            // GPG secret key.
            var (gok, gout, _) = await RunAsync("gpg", "--list-secret-keys --keyid-format=long", null, ct);
            if (gok)
            {
                // first "sec   rsa4096/<KEYID> ..." line
                var line = gout.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("sec"));
                if (line != null)
                {
                    var slash = line.IndexOf('/');
                    if (slash >= 0)
                        s.GpgKeyId = new string(line.Substring(slash + 1).TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
                }
            }

            if (!string.IsNullOrWhiteSpace(repo) && Directory.Exists(repo))
            {
                var helper = await _git.RawAsync(repo, new[] { "config", "--get", "credential.helper" }, ct);
                if (helper.Ok && helper.Output.Trim().Length > 0) s.CredentialHelper = helper.Output.Trim();

                var sign = await _git.RawAsync(repo, new[] { "config", "--get", "commit.gpgsign" }, ct);
                var key = await _git.RawAsync(repo, new[] { "config", "--get", "user.signingkey" }, ct);
                s.SigningEnabled = sign.Ok && sign.Output.Trim() == "true" && key.Ok && key.Output.Trim().Length > 0;
            }

            return s;
        }

        /// <summary>Point a repo at an SSH key so fetch/push authenticate without a prompt.</summary>
        public Task<GitResult> UseSshKeyAsync(string repo, string keyPath, CancellationToken ct = default) =>
            _git.RawAsync(repo, new[] { "config", "core.sshCommand", $"ssh -i \"{keyPath.Replace('\\', '/')}\" -o StrictHostKeyChecking=accept-new" }, ct);

        /// <summary>Turn on GPG-signed commits for a repo using a detected key.</summary>
        public async Task<GitResult> EnableSigningAsync(string repo, string gpgKeyId, CancellationToken ct = default)
        {
            var k = await _git.RawAsync(repo, new[] { "config", "user.signingkey", gpgKeyId }, ct);
            if (!k.Ok) return k;
            return await _git.RawAsync(repo, new[] { "config", "commit.gpgsign", "true" }, ct);
        }

        private static async Task<(bool ok, string outp, string err)> RunAsync(string exe, string args, string? cwd, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                if (cwd != null) psi.WorkingDirectory = cwd;
                using var proc = Process.Start(psi);
                if (proc == null) return (false, "", "start failed");
                var o = await proc.StandardOutput.ReadToEndAsync();
                var e = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync(ct);
                return (proc.ExitCode == 0, o, e);
            }
            catch (Exception ex) { return (false, "", ex.Message); }
        }
    }
}
