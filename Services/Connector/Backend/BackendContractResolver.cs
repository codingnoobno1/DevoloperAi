using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Backend
{
    /// <summary>
    /// Default <see cref="IBackendContractResolver"/>. Deterministic and dependency-free so it can be
    /// used directly from an MCP tool, the CLI, or the connector workbench. No AI, no network.
    /// </summary>
    public sealed class BackendContractResolver : IBackendContractResolver
    {
        private static readonly string[] SwaggerNames =
        {
            "swagger.json", "openapi.json", "swagger.yaml", "swagger.yml",
            "openapi.yaml", "openapi.yml", "api-docs.json"
        };

        public Task<BackendContract> ResolveAsync(string backendPath, CancellationToken ct = default)
            => Task.Run(() => Resolve(backendPath), ct);

        private BackendContract Resolve(string backendPath)
        {
            var contract = new BackendContract { BackendPath = backendPath ?? "" };

            if (string.IsNullOrWhiteSpace(backendPath) || !Directory.Exists(backendPath))
            {
                contract.Notes.Add($"Backend path does not exist: '{backendPath}'.");
                return contract;
            }

            contract.Stack = DetectStack(backendPath, contract);

            // 1. Prefer an explicit swagger / openapi contract.
            var swaggerFile = SourceWalker.FindFirst(backendPath, SwaggerNames);
            if (swaggerFile != null)
            {
                contract.SwaggerPath = swaggerFile;
                var routes = SwaggerContractParser.Parse(swaggerFile, out var swaggerNotes);
                contract.Notes.AddRange(swaggerNotes);

                if (routes.Count > 0)
                {
                    contract.PrimarySource = RouteSource.Swagger;
                    contract.Routes = routes;
                    return contract;
                }

                contract.Notes.Add("Swagger file found but yielded no routes; falling back to source scan.");
            }

            // 2. Fall back to scanning source for route declarations.
            var scanned = RouteScanner.Scan(backendPath, contract.Stack, out var scanNotes);
            contract.Notes.AddRange(scanNotes);
            contract.PrimarySource = RouteSource.RouteScan;
            contract.Routes = scanned;

            if (scanned.Count == 0)
                contract.Notes.Add("No routes detected. The connector can AI-generate a backend from the frontend's calls.");

            return contract;
        }

        // ── stack detection from manifest files ─────────────────────────────────────────
        // Recursively finds manifests (skipping node_modules/bin/obj/etc via SourceWalker) so a
        // solution with its projects in subfolders is still classified correctly — the previous
        // top-directory-only .csproj search was why a real ASP.NET repo read as "Unknown".
        private static readonly Regex ControllerMarkerRx = new(
            @"\[ApiController\]|\[Route\s*\(|\[Http(?:Get|Post|Put|Patch|Delete)\b|ControllerBase\b|:\s*Controller\b|MapControllers\b",
            RegexOptions.Compiled);

        private static BackendStack DetectStack(string root, BackendContract contract)
        {
            // .NET — any .csproj (searched recursively) that references the web SDK / ASP.NET Core.
            var csprojs = SourceWalker.EnumerateFiles(root, new[] { ".csproj" }, maxFiles: 200).ToList();
            foreach (var csproj in csprojs)
            {
                var text = SafeRead(csproj);
                if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase))
                {
                    contract.Notes.Add($"Detected ASP.NET Core ({Path.GetFileName(csproj)}).");
                    return BackendStack.AspNetCore;
                }
            }

            // Node / Express — package.json with an express dependency.
            var packageJson = SourceWalker.FindFirst(root, new[] { "package.json" }, maxDepth: 3);
            if (packageJson != null && SafeRead(packageJson).Contains("\"express\"", StringComparison.OrdinalIgnoreCase))
            {
                contract.Notes.Add("Detected Express (package.json).");
                return BackendStack.Express;
            }

            // Python — requirements.txt / pyproject.toml mentioning fastapi or flask/django.
            var pyReq = SourceWalker.FindFirst(root, new[] { "requirements.txt", "pyproject.toml" }, maxDepth: 3);
            if (pyReq != null)
            {
                var text = SafeRead(pyReq).ToLowerInvariant();
                if (text.Contains("fastapi")) { contract.Notes.Add("Detected FastAPI."); return BackendStack.FastApi; }
                if (text.Contains("flask")) { contract.Notes.Add("Detected Flask."); return BackendStack.Flask; }
                if (text.Contains("django")) { contract.Notes.Add("Detected Django."); return BackendStack.Django; }
            }

            // Java — Spring Boot via build.gradle / pom.xml.
            var javaBuild = SourceWalker.FindFirst(root, new[] { "build.gradle", "build.gradle.kts", "pom.xml" }, maxDepth: 3);
            if (javaBuild != null)
            {
                var text = SafeRead(javaBuild);
                if (text.Contains("spring-boot", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("springframework", StringComparison.OrdinalIgnoreCase))
                {
                    contract.Notes.Add("Detected Spring Boot.");
                    return BackendStack.SpringBoot;
                }
            }

            // .NET fallback — a C# project whose source carries controller/route attributes even when
            // the web SDK line lives in a host project we didn't open directly.
            if (csprojs.Count > 0 && HasAspNetControllers(root))
            {
                contract.Notes.Add("Detected ASP.NET Core (controller/route attributes in source).");
                return BackendStack.AspNetCore;
            }

            contract.Notes.Add("Backend stack not recognized; using heuristic multi-pattern scan.");
            return BackendStack.Unknown;
        }

        private static bool HasAspNetControllers(string root)
        {
            foreach (var file in SourceWalker.EnumerateFiles(root, new[] { ".cs" }, maxFiles: 500))
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                if (ControllerMarkerRx.IsMatch(text)) return true;
            }
            return false;
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return ""; }
        }
    }
}
