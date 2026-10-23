using System.Threading;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.Build
{
    /// <summary>
    /// Companion to <see cref="IBuildEngine"/> for the second half of the verification gate:
    /// compiling proves a patch is well-formed, running the suite proves it still behaves.
    /// Separate from <c>IBuildEngine</c> because tests are optional, far slower, and skippable.
    /// </summary>
    public interface ITestEngine
    {
        /// <summary>
        /// Whether <paramref name="projectPath"/> has a discoverable suite worth running. Checked
        /// before every run so projects without tests pay nothing for the gate.
        /// </summary>
        bool HasTests(string projectPath);

        /// <summary>
        /// Run the suite. Never throws for failing tests — a failure is a <see cref="TestResult"/>
        /// with <c>Ok == false</c>. Returns <c>Skipped</c> when there is no suite.
        /// </summary>
        Task<TestResult> TestAsync(string projectPath, CancellationToken ct = default);
    }
}
