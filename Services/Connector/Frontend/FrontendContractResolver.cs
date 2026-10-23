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
    /// resolve the same way across Web, Mobile (Flutter, Kotlin Android, React Native, MAUI), and
    /// native microservice clients (C++, Rust, Go).
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
                FrontendStack.NextJs or FrontendStack.React or FrontendStack.ReactNative or FrontendStack.Vite or FrontendStack.Angular
                    => FrontendApiExtractors.Web(frontendPath),
                FrontendStack.Blazor or FrontendStack.Maui => FrontendApiExtractors.Blazor(frontendPath),
                FrontendStack.KotlinAndroid => FrontendApiExtractors.KotlinJvm(frontendPath),
                FrontendStack.CppClient => FrontendApiExtractors.Cpp(frontendPath),
                FrontendStack.RustClient => FrontendApiExtractors.Rust(frontendPath),
                FrontendStack.GoClient => FrontendApiExtractors.Go(frontendPath),
                _ => FrontendApiExtractors.AllHeuristics(frontendPath)
            };

            contract.Calls = FrontendApiExtractors.Dedupe(calls);
            contract.Notes.Add($"Extracted {contract.Calls.Count} API call(s) for stack '{contract.Stack}'.");

            if (contract.Calls.Count == 0)
                contract.Notes.Add("No outbound API calls detected.");

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
                if (text.Contains("\"react-native\"", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("\"expo\"", StringComparison.OrdinalIgnoreCase))
                {
                    contract.Notes.Add("Detected React Native.");
                    return FrontendStack.ReactNative;
                }
                if (text.Contains("\"next\"", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected Next.js."); return FrontendStack.NextJs; }
                if (text.Contains("\"vite\"", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected Vite."); return FrontendStack.Vite; }
                if (text.Contains("@angular/core", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected Angular."); return FrontendStack.Angular; }
                if (text.Contains("\"react\"", StringComparison.OrdinalIgnoreCase)) { contract.Notes.Add("Detected React."); return FrontendStack.React; }
            }

            // Kotlin / Android / JVM Gradle
            var gradle = SourceWalker.FindFirst(root, new[] { "build.gradle", "build.gradle.kts", "pom.xml" }, maxDepth: 2);
            if (gradle != null)
            {
                contract.Notes.Add("Detected Kotlin / JVM Gradle project.");
                return FrontendStack.KotlinAndroid;
            }

            // .NET MAUI / Blazor / C#
            foreach (var csproj in SourceWalker.EnumerateFiles(root, new[] { ".csproj" }, maxFiles: 200))
            {
                var text = SafeRead(csproj);
                if (text.Contains("UseMaui", StringComparison.OrdinalIgnoreCase))
                {
                    contract.Notes.Add("Detected .NET MAUI.");
                    return FrontendStack.Maui;
                }
                if (text.Contains("Microsoft.NET.Sdk.Razor", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("Microsoft.AspNetCore.Components", StringComparison.OrdinalIgnoreCase))
                {
                    contract.Notes.Add("Detected Blazor.");
                    return FrontendStack.Blazor;
                }
            }

            if (SourceWalker.FindFirst(root, new[] { "Cargo.toml" }, maxDepth: 2) != null)
                return FrontendStack.RustClient;

            if (SourceWalker.FindFirst(root, new[] { "go.mod" }, maxDepth: 2) != null)
                return FrontendStack.GoClient;

            if (SourceWalker.FindFirst(root, new[] { "CMakeLists.txt", "vcpkg.json", "conanfile.txt" }, maxDepth: 2) != null)
                return FrontendStack.CppClient;

            contract.Notes.Add("Consumer stack not recognized; running all extractors.");
            return FrontendStack.Unknown;
        }

        private static string SafeRead(string path)
        {
            try { return File.ReadAllText(path); } catch { return ""; }
        }
    }
}
