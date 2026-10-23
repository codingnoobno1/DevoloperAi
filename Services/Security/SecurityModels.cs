using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.Security
{
    public enum Severity { Info = 0, Low = 1, Medium = 2, High = 3, Critical = 4 }

    /// <summary>One SAST finding: a rule matched at a source location.</summary>
    public sealed class SecurityFinding
    {
        public string RuleId { get; init; } = "";
        public string Title { get; init; } = "";
        public Severity Severity { get; init; }
        public string Category { get; init; } = "";
        public string File { get; init; } = "";      // relative to scan root
        public int Line { get; init; }
        public string Snippet { get; init; } = "";
        public string Why { get; init; } = "";
    }

    /// <summary>Result of scanning a folder tree.</summary>
    public sealed class SecurityScanReport
    {
        public string Root { get; init; } = "";
        public int FilesScanned { get; set; }
        public long ElapsedMs { get; set; }
        public List<SecurityFinding> Findings { get; init; } = new();

        /// <summary>Highest severity that causes the gate to fail (default High).</summary>
        public Severity FailOn { get; set; } = Severity.High;

        public IReadOnlyDictionary<Severity, int> Counts =>
            Findings.GroupBy(f => f.Severity).ToDictionary(g => g.Key, g => g.Count());

        public int Count(Severity s) => Findings.Count(f => f.Severity == s);

        /// <summary>CI-style gate: pass unless a finding at/above <see cref="FailOn"/> exists.</summary>
        public bool GatePassed => !Findings.Any(f => f.Severity >= FailOn);

        public string Summary =>
            $"{Findings.Count} finding(s): " +
            $"{Count(Severity.Critical)} critical, {Count(Severity.High)} high, {Count(Severity.Medium)} medium, " +
            $"{Count(Severity.Low)} low, {Count(Severity.Info)} info — gate {(GatePassed ? "PASS" : "FAIL")}";
    }
}
