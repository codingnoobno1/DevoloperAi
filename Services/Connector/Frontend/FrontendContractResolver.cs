using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Backend;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Frontend
{
    /// <summary>
    /// Default <see cref="IFrontendContractResolver"/>. Deterministic and dependency-free (no AI,
    /// no network), mirroring <see cref="BackendContractResolver"/> so both sides of the connector
    /// resolve the same way.
    /// </summary>
    public sealed class FrontendContractResolver : IFrontendContractResolver
    {
        public Task<FrontendContract> ResolveAsync(string frontendPath, CancellationToken ct = default)
            => Task.Run(() => Resolve(frontendPath), ct);

        private FrontendContract Resolve(string frontendPath)
        {
            var contract = new FrontendContract { FrontendPath = frontendPath ?? "" };

            if (string.IsNullOrWhiteSpace(frontendPath) || !Directory.Exists(frontendPath))
            {
                contract.Notes.Add($"Frontend path does not exist: '{frontendPath}'.");
                return contract;
            }

            contract.Stack = DetectStack(frontendPath, contract);

            List<ApiCallSite> calls = contract.Stack switch
            {
                FrontendStack.Flutter => FrontendApiExtractors.Flutter(frontendPath),
                FrontendStack.NextJs or FrontendStack.React or FrontendStack.Vite or FrontendStack.Angular
                    => FrontendApiExtractors.Web(frontendPath),
                FrontendStack.Blazor => FrontendApiExtractors.Blazor(frontendPath),
                _ => FrontendApiExtractors.AllHeuristics(frontendPath)
            };

            contract.Calls = FrontendApiExtractors.Dedupe(calls);
            contract.Notes.Add($"Extracted {contract.Calls.Count} API call(s) for stack '{contract.Stack}'.");

            if (contract.Calls.Count == 0)
                contract.Notes.Add("No API calls detected. The frontend may use a client this heuristic scanner doesn't recognize.");

            return contract;
        }

        private static FrontendStack DetectStack(string root, FrontendContract contract)
        {
            // Flutter — pubspec.yaml.
            if (SourceWalker.FindFirst(root, new[] { "pubspec.yaml", "pubspec.yml" }, maxDepth: 2) != null)
            {
                contract.Notes.Add("Detected Flutter (pubspec.yaml).");
                return FrontendStack.Flutter;
            }

            // Angular — angular.json.
            if (SourceWalker.FindFirst(root, new[] { "angular.json" }, maxDepth: 2) != null)
            {
                contract.Notes.Add("Detected Angular (angular.json).");
                return FrontendStack.Angular;
            }

            // JS/TS frameworks — package.json dependency sniffing.
            var packageJson = SourceWalker.FindFirst(root, new[] { "package.json" }, maxDepth: 2);
            if (packageJson != null)
            {
                var text = SafeRead(packageJson);
                if (text.Contains("\"next\"", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected Next.js."); return FrontendStack.NextJs; }
                if (text.Contains("\"vite\"", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected Vite."); return FrontendStack.Vite; }
                if (text.Contains("@angular/core", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected Angular."); return FrontendStack.Angular; }
                if (text.Contains("\"react\"", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected React."); return FrontendStack.React; }
            }

            // Blazor — a .csproj using the Razor SDK, or any .razor files present.
            if (HasBlazorMarkers(root))
            {
                contract.Notes.Add("Detected Blazor (.razor / Razor SDK).");
                return FrontendStack.Blazor;
            }

            contract.Notes.Add("Frontend stack not recognized; running all extractors.");
            return FrontendStack.Unknown;
        }

        private static bool HasBlazorMarkers(string root)
        {
            // Recursive (skips node_modules/bin/obj via SourceWalker) so projects in subfolders count.
            foreach (var csproj in SourceWalker.EnumerateFiles(root, new[] { ".csproj" }, maxFiles: 200))
            {
                var text = SafeRead(csproj);
                if (text.Contains("Microsoft.NET.Sdk.Razor", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Microsoft.AspNetCore.Components", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return SourceWalker.FindFirst(root, new[] { "_Imports.razor", "App.razor" }, maxDepth: 3) != null;
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return ""; }
        }
    }
}
