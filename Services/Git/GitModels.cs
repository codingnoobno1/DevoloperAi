using System.Collections.Generic;

namespace Syncro.Desktop.Services.Git
{
    /// <summary>Raw outcome of one git invocation.</summary>
    public sealed class GitResult
    {
        public bool Ok { get; init; }
        public int ExitCode { get; init; }
        public string Output { get; init; } = "";
        public string Error { get; init; } = "";

        public static GitResult Fail(string error) => new() { Ok = false, ExitCode = -1, Error = error };
    }

    /// <summary>One commit from <c>git log</c>.</summary>
    public sealed class GitCommit
    {
        public string Sha { get; init; } = "";
        public string ShortSha { get; init; } = "";
        public string Author { get; init; } = "";
        public string Email { get; init; } = "";
        public string Date { get; init; } = "";      // ISO-8601
        public string Subject { get; init; } = "";
    }

    /// <summary>A repo contributor with their commit count (from <c>git log --all</c>).</summary>
    public sealed class GitContributor
    {
        public string Name { get; init; } = "";
        public string Email { get; init; } = "";
        public int Commits { get; set; }
    }

    /// <summary>A local or remote branch, with its tracking state.</summary>
    public sealed class GitBranch
    {
        public string Name { get; init; } = "";
        public bool IsCurrent { get; init; }
        public bool IsRemote { get; init; }
        public string? Upstream { get; init; }
        public int Ahead { get; init; }
        public int Behind { get; init; }
        public bool Gone { get; init; }   // upstream was deleted
    }

    /// <summary>Configured git remote (e.g. origin).</summary>
    public sealed class GitRemote
    {
        public string Name { get; init; } = "";
        public string FetchUrl { get; set; } = "";
        public string PushUrl { get; set; } = "";
    }

    /// <summary>One changed path in the working tree (porcelain v2 + numstat).</summary>
    public sealed class GitFileChange
    {
        public string Path { get; init; } = "";
        public string State { get; init; } = "";     // e.g. "M", "A", "D", "??", "R", "U"
        public bool IsStaged { get; init; }
        public bool IsConflicted { get; init; }
        /// <summary>Two-letter unmerged code from porcelain v2 (UU, AA, DU, UD, AU, UA, DD); null when not conflicted.</summary>
        public string? ConflictCode { get; init; }
        public int Additions { get; set; }
        public int Deletions { get; set; }

        public string Directory
        {
            get
            {
                var norm = (Path ?? "").Replace('\\', '/');
                int idx = norm.LastIndexOf('/');
                return idx > 0 ? norm[..idx] : "(root)";
            }
        }

        public string FileName
        {
            get
            {
                var norm = (Path ?? "").Replace('\\', '/');
                int idx = norm.LastIndexOf('/');
                return idx >= 0 ? norm[(idx + 1)..] : norm;
            }
        }
    }

    /// <summary>Working-tree + upstream state.</summary>
    public sealed class GitStatus
    {
        public bool IsRepo { get; init; }
        public string Branch { get; init; } = "";
        public string? Upstream { get; init; }
        public int Ahead { get; init; }
        public int Behind { get; init; }
        public List<GitFileChange> Files { get; init; } = new();
        public bool IsClean => Files.Count == 0;
    }
}
