using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Syncro.Desktop.Services.Connector.ProjectDomain
{
    /// <summary>
    /// Discovers the individual projects inside a parent folder — the runtime replacement for
    /// hand-picking one backend + one frontend. Given a root (e.g. a mono-repo or a folder holding
    /// several repos), it finds each child directory that carries a recognizable project manifest and
    /// classifies it via <see cref="DomainDetector"/>. Nothing is hardcoded: the set of projects, their
    /// stacks and roles are all read from the file system at scan time.
    /// </summary>
    public sealed class WorkspaceDiscoveryService
    {
        // Manifest files that mark a directory as the root of a project across all supported ecosystems.
        private static readonly string[] Manifests =
        {
            "pubspec.yaml", "package.json", "requirements.txt", "pyproject.toml", "setup.py",
            "pom.xml", "build.gradle", "build.gradle.kts", "go.mod", "Cargo.toml",
            "CMakeLists.txt", "vcpkg.json", "conanfile.txt", "Makefile",
            "docker-compose.yml", "docker-compose.yaml"
        };

        private static readonly string[] SkipDirs =
        {
            "node_modules", "bin", "obj", ".git", ".idea", ".vs", ".vscode", "dist", "build",
            "target", "out", ".dart_tool", "packages", "vendor", "__pycache__", ".next"
        };

        /// <summary>
        /// Returns one node per project found under <paramref name="root"/>. If the root itself is a
        /// project, returns just that. Otherwise scans immediate children (and one level deeper for
        /// nested layouts). Each node is domain/stack-classified before return.
        /// </summary>
        public List<ConnectorWorkspaceNode> Discover(string root)
        {
            var nodes = new List<ConnectorWorkspaceNode>();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return nodes;

            // Case 1 — the root is itself a single project, unless it also contains multiple child projects.
            var childProjects = DiscoverChildren(root);
            if (childProjects.Count > 0)
            {
                return childProjects
                    .OrderBy(n => n.Wave)
                    .ThenBy(n => n.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (HasManifest(root))
            {
                nodes.Add(MakeNode(root));
            }

            return nodes;
        }

        private static List<ConnectorWorkspaceNode> DiscoverChildren(string root)
        {
            var nodes = new List<ConnectorWorkspaceNode>();
            foreach (var child in SafeDirs(root))
            {
                if (IsSkipped(child)) continue;

                if (HasManifest(child))
                {
                    nodes.Add(MakeNode(child));
                }
                else
                {
                    // one level deeper (e.g. apps/*, services/*, packages/*)
                    foreach (var grand in SafeDirs(child))
                    {
                        if (!IsSkipped(grand) && HasManifest(grand))
                            nodes.Add(MakeNode(grand));
                    }
                }
            }
            return nodes;
        }

        private static ConnectorWorkspaceNode MakeNode(string path)
        {
            var node = new ConnectorWorkspaceNode
            {
                Path = path,
                Label = new DirectoryInfo(path).Name
            };
            DomainDetector.Detect(node);   // fills Domain, Stack, Wave, RunCommand, Port
            return node;
        }

        private static bool HasManifest(string dir)
        {
            foreach (var m in Manifests)
                if (File.Exists(Path.Combine(dir, m))) return true;

            // .NET projects: any *.csproj or *.sln in the directory.
            try
            {
                if (Directory.EnumerateFiles(dir, "*.csproj").Any()) return true;
                if (Directory.EnumerateFiles(dir, "*.sln").Any()) return true;
            }
            catch { }
            return false;
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.EnumerateDirectories(dir); }
            catch { return Array.Empty<string>(); }
        }

        private static bool IsSkipped(string dir)
        {
            var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith('.')) return true;
            return SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase);
        }
    }
}
