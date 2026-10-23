using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// What a tool call touches, pulled from its JSON arguments without knowing the tool: file
    /// paths, URLs, shell script text, and the git repo/branch the paths live in.
    /// </summary>
    public sealed class ToolCallFacts
    {
        public List<string> Paths { get; } = new();
        public List<string> Urls { get; } = new();
        public List<string> Scripts { get; } = new();
        public string? Mode { get; set; }
        public string? RepoRoot { get; set; }
        public string? Branch { get; set; }
        public bool ParseFailed { get; set; }

        public int FileCount => Paths.Count;

        /// <summary>Short human description for the approval card and audit log.</summary>
        public string Target
        {
            get
            {
                if (Scripts.Count > 0) return FirstLine(Scripts[0]);
                if (Urls.Count > 0) return Urls[0];
                if (Paths.Count == 1) return Paths[0];
                if (Paths.Count > 1) return $"{Paths.Count} files under {CommonDir()}";
                return RepoRoot ?? "";
            }
        }

        private static string FirstLine(string s)
        {
            var line = s.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("@echo", StringComparison.OrdinalIgnoreCase)) ?? "";
            return line.Length > 120 ? line[..120] + "…" : line;
        }

        private string CommonDir()
        {
            var dirs = Paths.Select(p => System.IO.Path.GetDirectoryName(p) ?? p).ToList();
            var first = dirs[0];
            while (first.Length > 0 && !dirs.All(d => d.StartsWith(first, StringComparison.OrdinalIgnoreCase)))
                first = System.IO.Path.GetDirectoryName(first) ?? "";
            return first.Length > 0 ? first : "several folders";
        }

        // ── extraction ──

        private static readonly HashSet<string> RootKeys = new(StringComparer.OrdinalIgnoreCase)
        { "targetDirectory", "backendRoot", "root", "rootPath", "workspacePath", "projectRoot", "repo", "repoPath", "workspaceRoot", "directory", "dir", "cwd", "workingDirectory" };

        private static readonly HashSet<string> FileKeys = new(StringComparer.OrdinalIgnoreCase)
        { "absolutePath", "path", "filePath", "file", "targetPath", "outputPath", "destination" };

        private static readonly HashSet<string> UrlKeys = new(StringComparer.OrdinalIgnoreCase)
        { "url", "repoUrl", "endpoint", "baseUrl", "uri", "remoteUrl" };

        private static readonly HashSet<string> ScriptKeys = new(StringComparer.OrdinalIgnoreCase)
        { "scriptContent", "command", "script", "cmd", "commandLine" };

        public static ToolCallFacts From(string? json)
        {
            var f = new ToolCallFacts();
            var roots = new List<string>();
            var files = new List<string>();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try { Walk(JToken.Parse(json), null, f, roots, files); }
                catch { f.ParseFailed = true; }
            }

            string? baseRoot = roots.FirstOrDefault(r => System.IO.Path.IsPathRooted(r));
            foreach (var p in files.Concat(roots))
            {
                var full = p;
                if (!System.IO.Path.IsPathRooted(full) && baseRoot != null)
                {
                    try { full = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseRoot, full)); } catch { }
                }
                // Only real file targets count toward blast radius; roots are context.
                if (files.Contains(p) && !f.Paths.Contains(full, StringComparer.OrdinalIgnoreCase)) f.Paths.Add(full);
            }
            // A tool that names only a root (Commit, GitPush, RunBatchScript) still targets that folder.
            if (f.Paths.Count == 0 && baseRoot != null) f.Paths.Add(baseRoot);

            var probe = f.Paths.FirstOrDefault(System.IO.Path.IsPathRooted) ?? baseRoot;
            if (probe != null)
            {
                var (repo, branch) = GitHead.Resolve(probe);
                f.RepoRoot = repo;
                f.Branch = branch;
            }
            return f;
        }

        private static void Walk(JToken t, string? key, ToolCallFacts f, List<string> roots, List<string> files)
        {
            switch (t.Type)
            {
                case JTokenType.Object:
                    foreach (var prop in ((JObject)t).Properties()) Walk(prop.Value, prop.Name, f, roots, files);
                    break;
                case JTokenType.Array:
                    foreach (var item in t) Walk(item, key, f, roots, files);
                    break;
                case JTokenType.String:
                    var s = (string?)t ?? "";
                    if (s.Length == 0 || key == null) break;
                    if (string.Equals(key, "mode", StringComparison.OrdinalIgnoreCase)) f.Mode = s;
                    else if (ScriptKeys.Contains(key)) f.Scripts.Add(s);
                    else if (UrlKeys.Contains(key) || s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) f.Urls.Add(s);
                    else if (RootKeys.Contains(key)) roots.Add(s);
                    else if (FileKeys.Contains(key)) files.Add(s);
                    break;
            }
        }
    }

    /// <summary>Finds the enclosing git repo and current branch by reading .git/HEAD (no process spawn).</summary>
    public static class GitHead
    {
        public static (string? Repo, string? Branch) Resolve(string path)
        {
            try
            {
                var dir = System.IO.Directory.Exists(path) ? path : System.IO.Path.GetDirectoryName(path);
                while (!string.IsNullOrEmpty(dir))
                {
                    var dotGit = System.IO.Path.Combine(dir, ".git");
                    string? gitDir = null;
                    if (System.IO.Directory.Exists(dotGit)) gitDir = dotGit;
                    else if (System.IO.File.Exists(dotGit))
                    {
                        // worktree / submodule: ".git" is a file "gitdir: <path>"
                        var line = System.IO.File.ReadAllText(dotGit).Trim();
                        if (line.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                        {
                            var g = line[7..].Trim();
                            gitDir = System.IO.Path.IsPathRooted(g) ? g : System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, g));
                        }
                    }
                    if (gitDir != null)
                    {
                        var head = System.IO.Path.Combine(gitDir, "HEAD");
                        if (!System.IO.File.Exists(head)) return (dir, null);
                        var h = System.IO.File.ReadAllText(head).Trim();
                        const string refPrefix = "ref: refs/heads/";
                        return (dir, h.StartsWith(refPrefix) ? h[refPrefix.Length..] : "(detached)");
                    }
                    dir = System.IO.Path.GetDirectoryName(dir);
                }
            }
            catch { }
            return (null, null);
        }
    }
}
