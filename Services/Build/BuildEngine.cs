using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Workspace.Events;

namespace Syncro.Desktop.Services.Build
{
    /// <inheritdoc cref="IBuildEngine"/>
    public sealed class BuildEngine : IBuildEngine
    {
        private readonly IEventBus? _bus;

        /// <summary>Bus is optional so the engine stays usable standalone (CLI, tests).</summary>
        public BuildEngine(IEventBus? bus = null) => _bus = bus;

        /// <summary>Hard ceiling so a hung toolchain can never wedge the reactor.</summary>
        private const int TimeoutMs = 5 * 60 * 1000;

        public BuildStack Detect(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                return BuildStack.Unknown;

            // pubspec.yaml before package.json: a Flutter repo can carry both (web/ subfolder).
            if (File.Exists(Path.Combine(projectPath, "pubspec.yaml"))) return BuildStack.Flutter;

            if (HasAny(projectPath, "*.sln") || HasAny(projectPath, "*.csproj")) return BuildStack.DotNet;

            if (File.Exists(Path.Combine(projectPath, "package.json"))) return BuildStack.Node;

            if (File.Exists(Path.Combine(projectPath, "requirements.txt")) ||
                File.Exists(Path.Combine(projectPath, "pyproject.toml")) ||
                File.Exists(Path.Combine(projectPath, "setup.py")))
                return BuildStack.Python;

            return BuildStack.Unknown;
        }

        private static bool HasAny(string dir, string pattern)
        {
            try { return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly).Any(); }
            catch { return false; }
        }

        public async Task<BuildResult> BuildAsync(string projectPath, CancellationToken ct = default)
        {
            var stack = Detect(projectPath);
            var result = new BuildResult { ProjectPath = projectPath, Stack = stack };

            if (stack == BuildStack.Unknown)
            {
                result.Error = "No recognized build marker (sln/csproj, pubspec.yaml, package.json, requirements.txt).";
                Publish(result);
                return result;
            }

            var (exe, args) = CommandFor(stack, projectPath);
            result.Command = $"{exe} {args}";

            var sw = Stopwatch.StartNew();
            try
            {
                var (exitCode, output) = await ProcessRunner.RunAsync(exe, args, projectPath, ct, TimeoutMs);
                result.ExitCode = exitCode;
                result.RawOutput = output;
                result.Ok = exitCode == 0;
                result.Diagnostics.AddRange(BuildOutputParser.Parse(output, stack));

                // A non-zero exit with no parsed diagnostic still has to surface something actionable.
                if (!result.Ok && result.Diagnostics.Count == 0)
                    result.Diagnostics.Add(new BuildDiagnostic(
                        BuildSeverity.Error,
                        $"Build failed with exit code {exitCode}. See output.",
                        projectPath, 0, 0, "BUILD_FAILED"));
            }
            catch (OperationCanceledException)
            {
                result.Error = "Build cancelled.";
            }
            catch (Exception ex)
            {
                // Toolchain missing (dotnet/flutter not on PATH) lands here.
                result.Error = ex.Message;
            }
            finally
            {
                result.DurationMs = sw.ElapsedMilliseconds;
            }

            Publish(result);
            return result;
        }

        /// <summary>
        /// The command per stack. For Flutter and Node we deliberately run the *analyzer/type-checker*
        /// rather than a full artifact build: it surfaces the same diagnostics in seconds instead of
        /// minutes, which is what a save-triggered reactor needs.
        /// </summary>
        private static (string Exe, string Args) CommandFor(BuildStack stack, string projectPath) => stack switch
        {
            BuildStack.DotNet => ("dotnet", "build --nologo -v q"),
            BuildStack.Flutter => ("flutter", "analyze --no-fatal-infos"),
            BuildStack.Node => HasTypeScript(projectPath)
                ? ("npx", "tsc --noEmit")
                : ("npm", "run build --if-present"),
            BuildStack.Python => ("python", "-m compileall -q ."),
            _ => ("", "")
        };

        private static bool HasTypeScript(string projectPath) =>
            File.Exists(Path.Combine(projectPath, "tsconfig.json"));

        private void Publish(BuildResult r)
        {
            try
            {
                _bus?.Publish(new BuildDone(
                    r.ProjectPath, r.Ok, r.Stack.ToString(), r.ErrorCount, r.WarningCount, r.Summary));
            }
            catch { /* a throwing subscriber must never fail the build call */ }
        }
    }
}
