using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Syncro.Desktop.Services.Security
{
    /// <summary>A single SAST rule: a compiled regex plus metadata and the file types it applies to.</summary>
    public sealed class SecurityRule
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public Severity Severity { get; init; }
        public string Category { get; init; } = "";
        public string Why { get; init; } = "";
        public Regex Pattern { get; init; } = null!;
        /// <summary>File extensions the rule applies to (lowercase, with dot). Empty = all.</summary>
        public string[] Extensions { get; init; } = System.Array.Empty<string>();
    }

    /// <summary>
    /// The built-in rule set — OWASP-flavoured patterns for secrets, weak crypto, injection,
    /// insecure deserialization, disabled TLS and dangerous eval. Pure regex, no external tool or
    /// runtime, so it ships with the .NET app and runs identically on every platform.
    /// </summary>
    public static class SecurityRules
    {
        private const RegexOptions O = RegexOptions.Compiled | RegexOptions.IgnoreCase;
        private static readonly string[] Code = { ".cs", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".py", ".dart", ".java", ".go", ".rb", ".php" };

        public static readonly IReadOnlyList<SecurityRule> All = new List<SecurityRule>
        {
            // ── secrets ──
            new() { Id="SEC001", Title="AWS access key id", Severity=Severity.Critical, Category="secret",
                Why="A hardcoded AWS key grants account access if leaked.",
                Pattern=new Regex(@"AKIA[0-9A-Z]{16}", RegexOptions.Compiled) },
            new() { Id="SEC002", Title="Private key material", Severity=Severity.Critical, Category="secret",
                Why="A committed private key compromises everything it protects.",
                Pattern=new Regex(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----", RegexOptions.Compiled) },
            new() { Id="SEC003", Title="Hardcoded secret/token assignment", Severity=Severity.High, Category="secret",
                Why="Secrets belong in env/secret stores, not source.",
                Pattern=new Regex(@"(?:api[_-]?key|secret|access[_-]?token|auth[_-]?token|client[_-]?secret)\s*[:=]\s*[""'][A-Za-z0-9_\-\.]{16,}[""']", O) },
            new() { Id="SEC004", Title="Password/secret in config file", Severity=Severity.High, Category="secret",
                Why="Credentials in a committed config/.env leak with the source.",
                Pattern=new Regex(@"(?:password|pwd|passwd|secret|api[_-]?key)\s*=\s*\S{4,}", O),
                Extensions=new[]{".env",".config",".json",".yml",".yaml",".xml",".properties",".ini"} },
            new() { Id="SEC006", Title="Credentials embedded in URL", Severity=Severity.High, Category="secret",
                Why="A user:password@host URL leaks credentials wherever the URL appears.",
                Pattern=new Regex(@"[a-z][a-z0-9+.\-]*://[A-Za-z0-9._%\-]+:[^@\s/""']{3,}@", O) },
            new() { Id="SEC005", Title="Hardcoded password literal", Severity=Severity.Medium, Category="secret",
                Why="Hardcoded passwords are a common breach vector.",
                Pattern=new Regex(@"(?:password|passwd)\s*[:=]\s*[""'][^""']{3,}[""']", O), Extensions=Code },

            // ── weak crypto ──
            new() { Id="SEC010", Title="Weak hash (MD5/SHA1)", Severity=Severity.Medium, Category="crypto",
                Why="MD5/SHA1 are broken for security use.",
                Pattern=new Regex(@"\b(?:MD5|SHA1)\b\s*[\.\(]", O), Extensions=Code },
            new() { Id="SEC011", Title="Weak cipher (DES/RC4/ECB)", Severity=Severity.Medium, Category="crypto",
                Why="DES/RC4/ECB provide no real confidentiality.",
                Pattern=new Regex(@"\b(?:DES|RC4|ECB)\b", RegexOptions.Compiled), Extensions=Code },

            // ── injection ──
            new() { Id="SEC020", Title="SQL built by string concatenation", Severity=Severity.High, Category="injection",
                Why="Concatenating user input into SQL enables SQL injection; use parameters.",
                Pattern=new Regex(@"(?:SELECT|INSERT|UPDATE|DELETE)\b[^;""']*[""']\s*\+\s*\w", O), Extensions=Code },
            new() { Id="SEC021", Title="SQL built with f-string/interpolation", Severity=Severity.High, Category="injection",
                Why="Interpolated SQL enables injection; use parameters.",
                Pattern=new Regex(@"(?:execute|cursor|query)\s*\(\s*f?[""'`][^""'`]*\$?\{?[^""'`]*(?:SELECT|INSERT|UPDATE|DELETE)", O), Extensions=Code },
            new() { Id="SEC030", Title="Command execution with dynamic input", Severity=Severity.High, Category="injection",
                Why="Building a shell command from variables enables command injection.",
                Pattern=new Regex(@"(?:os\.system|subprocess\.(?:call|run|Popen)|child_process\.exec)\s*\(\s*[^)]*(?:\+|\$\{|f[""'])", O), Extensions=Code },

            // ── deserialization ──
            new() { Id="SEC040", Title="Insecure deserialization", Severity=Severity.High, Category="deserialization",
                Why="Deserializing untrusted data can execute code.",
                Pattern=new Regex(@"\b(?:BinaryFormatter|pickle\.loads|ObjectInputStream|yaml\.load\s*\((?![^)]*Loader))", O), Extensions=Code },

            // ── transport / TLS ──
            new() { Id="SEC050", Title="TLS certificate validation disabled", Severity=Severity.High, Category="transport",
                Why="Disabling cert validation defeats TLS and enables MITM.",
                Pattern=new Regex(@"verify\s*=\s*False|rejectUnauthorized\s*:\s*false|InsecureSkipVerify\s*:\s*true|ServerCertificateValidationCallback\s*(?:\+?=|=>)", O), Extensions=Code },

            // ── dangerous eval / xss ──
            new() { Id="SEC060", Title="Dynamic code execution (eval/exec)", Severity=Severity.Medium, Category="eval",
                Why="eval/exec on dynamic input is a code-execution risk.",
                Pattern=new Regex(@"\b(?:eval|exec)\s*\(", RegexOptions.Compiled), Extensions=new[]{".js",".jsx",".ts",".tsx",".mjs",".py"} },
            new() { Id="SEC061", Title="React dangerouslySetInnerHTML", Severity=Severity.Low, Category="xss",
                Why="Injecting raw HTML risks XSS; sanitize first.",
                Pattern=new Regex(@"dangerouslySetInnerHTML", RegexOptions.Compiled), Extensions=new[]{".tsx",".jsx"} },

            // ── config ──
            new() { Id="SEC080", Title="Debug mode enabled", Severity=Severity.Info, Category="config",
                Why="Debug mode in production leaks internals.",
                Pattern=new Regex(@"\bdebug\s*[:=]\s*true\b", O), Extensions=new[]{".py",".env",".json",".yml",".yaml",".cs",".ts"} },
        };
    }
}
