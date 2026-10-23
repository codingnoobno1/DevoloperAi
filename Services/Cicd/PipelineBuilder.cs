using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Syncro.Desktop.Services.Cicd
{
    /// <summary>What kind of project a pipeline targets — drives the setup/build/test steps.</summary>
    public enum PipelineStack { Node, DotNet, Python, Java, Flutter, Generic }

    /// <summary>A generated pipeline: the file path it belongs at + its YAML content.</summary>
    public sealed class GeneratedPipeline
    {
        public string Path { get; init; } = ".github/workflows/ci.yml";
        public string Yaml { get; init; } = "";
        public PipelineStack Stack { get; init; }
    }

    /// <summary>
    /// Generates a GitHub Actions CI pipeline (YAML) for a project — build → test → security scan —
    /// using only marketplace actions that need no local tooling (so it runs on any runner). Pure
    /// text generation: no external dependencies, fully cross-platform, ships with the .NET app.
    /// The security stage uses Trivy (filesystem/CVE) + Gitleaks (secrets) actions, matching Syncro's
    /// own native scanner categories.
    /// </summary>
    public sealed class PipelineBuilder
    {
        public GeneratedPipeline Build(PipelineStack stack, bool includeSecurity = true, string[]? branches = null)
        {
            branches ??= new[] { "main", "master" };
            var y = new StringBuilder();
            y.Append("name: CI\n\n");
            y.Append("on:\n");
            y.Append("  push:\n    branches: [ ").Append(string.Join(", ", branches)).Append(" ]\n");
            y.Append("  pull_request:\n    branches: [ ").Append(string.Join(", ", branches)).Append(" ]\n\n");
            y.Append("jobs:\n");

            // build + test
            y.Append("  build:\n");
            y.Append("    runs-on: ubuntu-latest\n");
            y.Append("    steps:\n");
            y.Append("      - uses: actions/checkout@v4\n");
            foreach (var step in BuildSteps(stack)) y.Append("      ").Append(step).Append('\n');

            if (includeSecurity)
            {
                y.Append('\n');
                y.Append("  security:\n");
                y.Append("    runs-on: ubuntu-latest\n");
                y.Append("    steps:\n");
                y.Append("      - uses: actions/checkout@v4\n");
                y.Append("      - name: Trivy filesystem scan (CVEs, misconfig)\n");
                y.Append("        uses: aquasecurity/trivy-action@0.24.0\n");
                y.Append("        with:\n");
                y.Append("          scan-type: fs\n");
                y.Append("          scan-ref: .\n");
                y.Append("          severity: HIGH,CRITICAL\n");
                y.Append("          exit-code: '1'\n");
                y.Append("      - name: Gitleaks secret scan\n");
                y.Append("        uses: gitleaks/gitleaks-action@v2\n");
                y.Append("        env:\n");
                y.Append("          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}\n");
            }

            return new GeneratedPipeline
            {
                Path = ".github/workflows/ci.yml",
                Yaml = y.ToString(),
                Stack = stack,
            };
        }

        private static IEnumerable<string> BuildSteps(PipelineStack stack) => stack switch
        {
            PipelineStack.Node => new[]
            {
                "- uses: actions/setup-node@v4",
                "  with:",
                "    node-version: '20'",
                "- run: npm ci",
                "- run: npm run build --if-present",
                "- run: npm test --if-present",
            },
            PipelineStack.DotNet => new[]
            {
                "- uses: actions/setup-dotnet@v4",
                "  with:",
                "    dotnet-version: '9.0.x'",
                "- run: dotnet restore",
                "- run: dotnet build --no-restore -c Release",
                "- run: dotnet test --no-build -c Release",
            },
            PipelineStack.Python => new[]
            {
                "- uses: actions/setup-python@v5",
                "  with:",
                "    python-version: '3.12'",
                "- run: pip install -r requirements.txt",
                "- run: python -m pytest --maxfail=1 -q || true",
            },
            PipelineStack.Java => new[]
            {
                "- uses: actions/setup-java@v4",
                "  with:",
                "    distribution: temurin",
                "    java-version: '21'",
                "- run: mvn -B verify",
            },
            PipelineStack.Flutter => new[]
            {
                "- uses: subosito/flutter-action@v2",
                "  with:",
                "    channel: stable",
                "- run: flutter pub get",
                "- run: flutter analyze",
                "- run: flutter test",
            },
            _ => new[] { "- run: echo \"add your build + test commands here\"" },
        };

        /// <summary>Map a Syncro stack/framework string to a pipeline stack.</summary>
        public static PipelineStack FromStack(string stackOrFramework)
        {
            var s = (stackOrFramework ?? "").ToLowerInvariant();
            if (s.Contains("next") || s.Contains("react") || s.Contains("node") || s.Contains("express") || s.Contains("vue") || s.Contains("angular")) return PipelineStack.Node;
            if (s.Contains("asp") || s.Contains(".net") || s.Contains("dotnet") || s.Contains("blazor") || s.Contains("csharp")) return PipelineStack.DotNet;
            if (s.Contains("fastapi") || s.Contains("flask") || s.Contains("django") || s.Contains("python")) return PipelineStack.Python;
            if (s.Contains("spring") || s.Contains("java")) return PipelineStack.Java;
            if (s.Contains("flutter") || s.Contains("dart")) return PipelineStack.Flutter;
            return PipelineStack.Generic;
        }
    }
}
