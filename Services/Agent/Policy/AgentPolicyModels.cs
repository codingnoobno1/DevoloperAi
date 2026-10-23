using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>How much the agent may do without asking (agentsettings.md §C.8.1).</summary>
    public enum AutonomyLevel { Observe, Suggest, GuardedAuto, AutoNotify, FullAutoSandbox }

    /// <summary>What a tool call does to the world. Drives the base risk score.</summary>
    public enum ActionClass { Read, Internal, LocalWrite, Execute, Outward, Destructive }

    /// <summary>Outcome of the bypass decision for one tool call.</summary>
    public enum Decision { Auto, AutoNotify, Ask, AskDoubleConfirm, Block }

    /// <summary>Per-tool override set in Tools &amp; Capabilities.</summary>
    public enum ToolOverride { Default, AlwaysAsk, AutoIfSafe, Block }

    /// <summary>Outcome of one step in a conflict resolution chain.</summary>
    public enum StepOutcome { Resolved, Partial, Failed }

    /// <summary>
    /// The single settings document for the agent. Persisted as JSON at
    /// %LOCALAPPDATA%\SyncroDesktop\agent\policy.json by <see cref="AgentPolicyStore"/>,
    /// and exportable/importable as .syncro/agent-policy.yaml.
    /// </summary>
    public sealed class AgentPolicy
    {
        public int Version { get; set; } = 2;
        public string Scope { get; set; } = "Global";
        public ModelSettings Model { get; set; } = new();
        public ContextRetrievalSettings Context { get; set; } = new();
        public BypassSettings Bypass { get; set; } = new();
        public PermissionsSettings Permissions { get; set; } = new();
        public PrivacySettings Privacy { get; set; } = new();
        public EnvironmentSettings Environments { get; set; } = new();
        public ApprovalSettings Approvals { get; set; } = new();
        public ConflictSettings Conflicts { get; set; } = ConflictSettings.CreateDefaults();
        public CherryPickSettings CherryPick { get; set; } = new();
        public GitSyncSettings GitSync { get; set; } = new();
        public CommitStyleSettings CommitStyle { get; set; } = new();
        public PrSettings PullRequests { get; set; } = new();
        public AutomationSettings Automation { get; set; } = new();
        public PlatformAgentSettings PlatformAgent { get; set; } = new();
    }

    public sealed class ModelSettings
    {
        public string Provider { get; set; } = "Groq";
        public string AgentModel { get; set; } = "openai/gpt-oss-120b";
        public string ResolverModel { get; set; } = "";          // empty = same as agent
        public string FallbackModel { get; set; } = "";          // empty = none
        public double AgentTemperature { get; set; } = 0.2;
        public double ResolverTemperature { get; set; } = 0.0;
        public int MaxToolCalls { get; set; } = 25;
        public int TokensPerConflictHunk { get; set; } = 4000;
        public int DailyTokenBudget { get; set; } = 250_000;     // 0 = unlimited
        public List<string> ContextSources { get; set; } = new() { "both-sides", "base", "ast-neighbours", "commit-messages", "linked-contract" };
        public bool RedactSecrets { get; set; } = true;
        public bool OfflineMode { get; set; }
    }

    public sealed class ContextRetrievalSettings
    {
        public int MaxContextPackTokens { get; set; } = 8000;
        public int AstRankingWeight { get; set; } = 50;
        public int RecencyWeight { get; set; } = 25;
        public int GraphDistanceWeight { get; set; } = 25;
        public string GlobalSystemPrompt { get; set; } =
            "You are the Syncro AI SRE & Software Engineering Agent for {workspace}. Always prefer deterministic Git and structural AST operations before proposing LLM code edits, and never bypass verification gates on {projects}.";
        public string ScopeSystemPrompt { get; set; } =
            "Enforce multi-project API contract stability across producer and consumer services. Preserve existing public route signatures unless both sides are updated.";
        public string ResolverPromptTemplate { get; set; } =
            "Resolve the 3-way conflict (BASE, OURS, THEIRS) for {filePath} ({conflictType}). Output valid JSON: { \"resolvedText\": \"...\", \"confidence\": 0.0-1.0, \"rationale\": \"...\", \"needsHuman\": false }.";
    }

    public sealed class RiskFactorWeights
    {
        public int OutwardAction { get; set; } = 35;
        public int ConsumerBlastRadius { get; set; } = 20;
        public int ProtectedBranch { get; set; } = 30;
        public int LowAiConfidence { get; set; } = 20;
        public int ProductionEnvironment { get; set; } = 40;
        public int VerificationPassedBonus { get; set; } = -10;
        public int RerereKnownBonus { get; set; } = -10;
    }

    public sealed class BypassSettings
    {
        public AutonomyLevel Level { get; set; } = AutonomyLevel.Suggest;   // safe default: every change asks
        public int AutoMax { get; set; } = 20;
        public int NotifyMax { get; set; } = 40;
        public int AskMax { get; set; } = 70;
        public RiskFactorWeights Weights { get; set; } = new();
        public List<string> ProtectedBranches { get; set; } = new() { "main", "master", "release/*" };
        /// <summary>Folders where Full-auto (sandbox) may act freely, e.g. throwaway worktrees.</summary>
        public List<string> SandboxRoots { get; set; } = new();
        /// <summary>
        /// Keys must be real tool names (see Tools &amp; Capabilities). No "auto if safe" defaults: that
        /// override lifts a tool above the Suggest level, which would undo the safe default.
        /// </summary>
        public Dictionary<string, ToolOverride> ToolOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["GitPush"] = ToolOverride.AlwaysAsk,
        };
    }

    public sealed class PermissionsSettings
    {
        /// <summary>Shell lines allowed without asking. Prefix match, or /regex/.</summary>
        public List<string> ShellAllow { get; set; } = new()
        {
            "git status", "git log", "git diff", "git fetch", "git cherry -v", "git range-diff",
            "dotnet build", "dotnet test", "dotnet restore", "npm run build", "npm test",
            "flutter analyze", "flutter test", "echo", "dir", "ls"
        };
        /// <summary>Shell lines that are always blocked.</summary>
        public List<string> ShellDeny { get; set; } = new()
        {
            "git push --force", "git push -f", "git reset --hard", "git clean -fd", "rm -rf", "rd /s", "rmdir /s",
            "del /s", "/^format\\s+[a-z]:/", "Remove-Item -Recurse", "/curl .*\\|\\s*(sh|bash|iex)/", "/iwr .*\\|\\s*iex/"
        };
        /// <summary>If not empty, writes outside these globs need approval.</summary>
        public List<string> PathAllow { get; set; } = new();
        /// <summary>Writes matching these globs are always blocked.</summary>
        public List<string> PathDeny { get; set; } = new() { "**/.git/**", "**/*.pfx", "**/*.snk", "**/.env.production" };
        /// <summary>Hosts the agent may call without asking. Subdomains match.</summary>
        public List<string> NetworkAllow { get; set; } = new()
        {
            "localhost", "127.0.0.1", "github.com", "api.github.com", "raw.githubusercontent.com", "project-syncroo.netlify.app"
        };
    }

    public sealed class PrivacySettings
    {
        public List<string> LlmDenyPathGlobs { get; set; } = new() { "**/.env*", "**/*.pem", "**/*.key", "**/*.pfx", "**/secrets/**" };
        public int MaxRequestKilobytes { get; set; } = 64;
        public List<string> LocalOnlyProjects { get; set; } = new();
        public bool RequireRepoPolicyTrust { get; set; } = true;
        public List<string> TrustedRepoPolicies { get; set; } = new() { @"D:\DeveloperAI\DEVOLOPER\Syncro.Desktop" };
        public bool LockToollessAiResolver { get; set; } = true;
    }

    public sealed class EnvironmentSettings
    {
        /// <summary>Branch glob → local | staging | production.</summary>
        public List<EnvironmentRule> Branches { get; set; } = new()
        {
            new() { Pattern = "main", Environment = "production" },
            new() { Pattern = "master", Environment = "production" },
            new() { Pattern = "release/*", Environment = "production" },
            new() { Pattern = "staging", Environment = "staging" },
        };
        public bool FreezeNow { get; set; }
        public string FreezeReason { get; set; } = "";
        public List<ScheduledFreezeWindow> ScheduledFreezes { get; set; } = new()
        {
            new() { Name = "Weekend Production Freeze", Cron = "0 18 * * FRI", DurationHours = 64, Reason = "No autonomous deploys Fri 18:00 – Mon 10:00", Enabled = true }
        };
        public bool BreakGlassActive { get; set; }
        public string BreakGlassIncidentId { get; set; } = "";
        public string BreakGlassReason { get; set; } = "";
    }

    public sealed class ScheduledFreezeWindow
    {
        public string Name { get; set; } = "";
        public string Cron { get; set; } = "0 18 * * FRI";
        public int DurationHours { get; set; } = 64;
        public string Reason { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

    public sealed class EnvironmentRule
    {
        public string Pattern { get; set; } = "";
        public string Environment { get; set; } = "local";
    }

    public sealed class ApprovalSettings
    {
        /// <summary>Unanswered approvals are denied after this many minutes. Never auto-approved.</summary>
        public int TimeoutMinutes { get; set; } = 30;
        public bool AllowBatchApprove { get; set; } = true;
        public bool NotifyOnAutoNotify { get; set; } = true;
    }

    /// <summary>Merge &amp; Conflict Resolution configuration (agentsettings.md §C.5–C.7).</summary>
    public sealed class ConflictSettings
    {
        public string FileOrder { get; set; } = "ast-dependency";     // ast-dependency | path | smallest-first | risk-lowest-first
        public string RuleEvaluation { get; set; } = "first-match";   // first-match | best-score
        public int MaxStepAttempts { get; set; } = 1;
        public List<string> GlobalPrefix { get; set; } = new() { "T6", "T12" };
        public List<string> GlobalSuffix { get; set; } = new() { "H1" };
        public bool StopOnCriticalUnresolved { get; set; } = true;

        public List<ConflictTypeDef> ConflictTypes { get; set; } = new();
        public List<TechniqueDef> Techniques { get; set; } = new();
        public List<ResolutionRule> Rules { get; set; } = new();
        public List<GateDef> Gates { get; set; } = new();

        public static ConflictSettings CreateDefaults()
        {
            var s = new ConflictSettings();
            s.ConflictTypes = DefaultConflictTypes();
            s.Techniques = DefaultTechniques();
            s.Gates = DefaultGates();
            s.Rules = DefaultRules();
            return s;
        }

        public static List<(string Code, string Name, string Meaning)> StructuralGitCodes { get; } = new()
        {
            ("UU", "Both modified", "Same region edited on both sides — common 3-way conflict"),
            ("AA", "Both added", "Same file path created independently on both branches"),
            ("DU", "Deleted by us", "Deleted on current branch, modified on incoming branch"),
            ("UD", "Deleted by them", "Deleted on incoming branch, modified on current branch"),
            ("AU", "Added by us", "Added by us, conflicting state on incoming branch"),
            ("UA", "Added by them", "Added by them, conflicting state on current branch"),
            ("DD", "Both deleted", "Deleted on both sides (often rename collision)"),
            ("RR", "Rename / rename", "Same file renamed to two different paths"),
            ("R/D", "Rename / delete", "Renamed on one side, deleted on the other"),
            ("DIR", "File / directory", "Path is a file on one side and a directory on the other"),
            ("MODE", "Mode change", "Executable bit or symlink mode differs"),
            ("SUB", "Submodule pointer", "Submodule points to two diverging commits"),
            ("BIN", "Binary file", "Binary payload conflict — no line markers possible")
        };

        public static List<ConflictTypeDef> DefaultConflictTypes() => new()
        {
            new() { Id = "secrets/env", Label = "Secrets & .env Files", Globs = new() { "**/.env*", "**/*.pem", "**/*.key", "**/*.pfx" }, Priority = 100, Severity = "critical", IsBuiltin = true, Description = "Secret or credential files — never auto-resolved." },
            new() { Id = "whitespace-only", Label = "Whitespace-Only Diff", Globs = new() { "**/*" }, Priority = 95, Severity = "low", IsBuiltin = true, Description = "Conflict disappears under -X ignore-space-change." },
            new() { Id = "lockfile", Label = "Package Lockfile", Globs = new() { "**/package-lock.json", "**/yarn.lock", "**/pnpm-lock.yaml", "**/pubspec.lock", "**/packages.lock.json", "**/Cargo.lock", "**/poetry.lock" }, Priority = 90, Severity = "low", IsBuiltin = true, Description = "Deterministic lockfiles regenerated from merged manifests." },
            new() { Id = "generated", Label = "Generated / Codegen Output", Globs = new() { "**/*.g.dart", "**/*.freezed.dart", "**/*.Designer.cs", "**/obj/**", "**/dist/**" }, ContentRegex = "auto-generated|DO NOT EDIT", Priority = 85, Severity = "low", IsBuiltin = true, Description = "Compiler or codegen output files." },
            new() { Id = "changelog", Label = "Changelog & Release Notes", Globs = new() { "**/CHANGELOG.md", "**/RELEASE_NOTES*" }, Priority = 80, Severity = "low", IsBuiltin = true, Description = "Keep both entries via union merge." },
            new() { Id = "imports", Label = "Imports / Using Block Only", Globs = new() { "**/*.cs", "**/*.ts", "**/*.tsx", "**/*.dart", "**/*.py", "**/*.go" }, AstNodeKind = "ImportDeclaration", Priority = 75, Severity = "low", IsBuiltin = true, Description = "Hunk only touches import/using directives." },
            new() { Id = "version-bump", Label = "Semver Version Bump", Globs = new() { "**/*.csproj", "**/package.json", "**/pubspec.yaml", "**/version.json" }, ContentRegex = "\"version\"|<Version>", Priority = 70, Severity = "low", IsBuiltin = true, Description = "Single version string increment." },
            new() { Id = "manifest-deps", Label = "Dependency Manifest", Globs = new() { "**/package.json", "**/pubspec.yaml", "**/*.csproj", "**/Cargo.toml" }, Priority = 65, Severity = "medium", IsBuiltin = true, Description = "Package dependency declarations." },
            new() { Id = "config-json-yaml", Label = "JSON / YAML Configuration", Globs = new() { "**/*.json", "**/*.yaml", "**/*.yml", "**/appsettings*.json" }, Priority = 60, Severity = "medium", IsBuiltin = true, Description = "Structured key-value configuration files." },
            new() { Id = "db-migration", Label = "Database Migration", Globs = new() { "**/Migrations/**", "**/migrations/**", "**/*_migration.*" }, Priority = 55, Severity = "high", IsBuiltin = true, Description = "EF Core / Prisma / SQL schema migrations." },
            new() { Id = "api-contract", Label = "API Route / Contract Surface", Globs = new() { "**/*Controller*.cs", "**/routes/**", "**/openapi*.json", "**/swagger*.json", "**/api/**" }, Priority = 50, Severity = "high", IsBuiltin = true, Description = "Producer HTTP route or DTO linked in Connector graph." },
            new() { Id = "infra", Label = "Infrastructure & CI/CD", Globs = new() { "**/*.tf", "**/Dockerfile*", "**/docker-compose*.yml", "**/.github/workflows/*.yml", "**/helm/**" }, Priority = 45, Severity = "high", IsBuiltin = true, Description = "Terraform, Docker, Kubernetes, and CI workflow files." },
            new() { Id = "binary-asset", Label = "Binary Asset", Globs = new() { "**/*.png", "**/*.jpg", "**/*.woff2", "**/*.pdf", "**/*.pptx" }, Priority = 40, Severity = "medium", IsBuiltin = true, Description = "Non-text media or binary assets." },
            new() { Id = "formatting-only", Label = "Formatting-Only Diff", Globs = new() { "**/*.cs", "**/*.ts", "**/*.dart", "**/*.js" }, Priority = 35, Severity = "low", IsBuiltin = true, Description = "Conflict disappears after running stack code formatter." },
            new() { Id = "code-logic", Label = "Source Code Logic", Globs = new() { "**/*.cs", "**/*.razor", "**/*.ts", "**/*.tsx", "**/*.js", "**/*.py", "**/*.dart", "**/*.rs", "**/*.go", "**/*.cpp", "**/*.kt" }, Priority = 20, Severity = "medium", IsBuiltin = true, Description = "General source code methods, classes, and statements." }
        };

        public static List<TechniqueDef> DefaultTechniques() => new()
        {
            // Git-native (T1–T15)
            new() { Id = "T1", Label = "ort Default 3-Way Merge", Category = "git-native", CommandOrHint = "git merge -s ort", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T2", Label = "Histogram / Patience Diff", Category = "git-native", CommandOrHint = "git merge -s ort -X diff-algorithm=histogram", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T3", Label = "Ignore Space Change", Category = "git-native", CommandOrHint = "git merge -X ignore-space-change", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T4", Label = "Renormalize Line Endings", Category = "git-native", CommandOrHint = "git merge -X renormalize", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T5", Label = "Rename Detection 40%", Category = "git-native", CommandOrHint = "git merge -X find-renames=40%", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T6", Label = "zdiff3 Conflict Markers", Category = "git-native", CommandOrHint = "git config merge.conflictStyle zdiff3", Risk = "none", Reversible = true, IsBuiltin = true },
            new() { Id = "T7", Label = "Prefer Side per Hunk (-X ours/theirs)", Category = "git-native", CommandOrHint = "git merge -X theirs", Risk = "medium", Reversible = true, IsBuiltin = true },
            new() { Id = "T8", Label = "Take Whole File (--ours / --theirs)", Category = "git-native", CommandOrHint = "git checkout --theirs -- <file>", Risk = "medium", Reversible = true, IsBuiltin = true },
            new() { Id = "T9", Label = "Strategy Ours (-s ours)", Category = "git-native", CommandOrHint = "git merge -s ours", Risk = "high", Reversible = true, IsBuiltin = true },
            new() { Id = "T10", Label = "Union Line Merge", Category = "git-native", CommandOrHint = "git merge-file --union", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T11", Label = "Custom Merge Driver (%O %A %B)", Category = "git-native", CommandOrHint = "merge.<driver>.driver", Risk = "medium", Reversible = true, IsBuiltin = true },
            new() { Id = "T12", Label = "Git Rerere Recorded Resolution", Category = "git-native", CommandOrHint = "git rerere", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T13", Label = "Single-File Re-Merge (merge-file)", Category = "git-native", CommandOrHint = "git merge-file -L ours -L base -L theirs", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T14", Label = "Octopus Multi-Branch Merge", Category = "git-native", CommandOrHint = "git merge -s octopus", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "T15", Label = "Subtree Merge (-X subtree)", Category = "git-native", CommandOrHint = "git merge -X subtree=<path>", Risk = "medium", Reversible = true, IsBuiltin = true },

            // Structural / Semantic (S1–S9)
            new() { Id = "S1", Label = "Regenerate Lockfile", Category = "structural", CommandOrHint = "npm install --package-lock-only / dotnet restore / flutter pub get", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "S2", Label = "Regenerate Codegen", Category = "structural", CommandOrHint = "dart run build_runner build --delete-conflicting-outputs", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "S3", Label = "Key-Level JSON / YAML 3-Way Merge", Category = "structural", CommandOrHint = "ConfigFileParser 3-way AST key merge", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "S4", Label = "AST Import / Using Union & Sort", Category = "structural", CommandOrHint = "AST union + deduplicate + sort directives", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "S5", Label = "Semver Max Picker", Category = "structural", CommandOrHint = "Select higher semantic version across branches", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "S6", Label = "AST Declaration Structural Merge", Category = "structural", CommandOrHint = "Mergiraf-style AST declaration merge (Services/AST)", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "S7", Label = "Format-Then-Merge", Category = "structural", CommandOrHint = "dotnet format / prettier / dart format on base, ours, theirs", Risk = "low", Reversible = true, IsBuiltin = true },
            new() { Id = "S8", Label = "DB Migration Renumbering", Category = "structural", CommandOrHint = "Renumber local migration timestamp after incoming & rebuild snapshot", Risk = "medium", Reversible = true, IsBuiltin = true },
            new() { Id = "S9", Label = "Contract-Aware Merge Verification", Category = "structural", CommandOrHint = "ContractEngine + MultiProjectScanService consumer check", Risk = "low", Reversible = true, IsBuiltin = true },

            // AI Techniques (A1–A4)
            new() { Id = "A1", Label = "AI Hunk Resolver (zdiff3 + AST)", Category = "ai", CommandOrHint = "LLM resolves conflict hunk with base/ours/theirs + AST neighbours", Risk = "medium", Reversible = true, IsBuiltin = true },
            new() { Id = "A2", Label = "AI Dual-Intent Rewrite", Category = "ai", CommandOrHint = "LLM synthesizes both commit intents into unified region", Risk = "high", Reversible = true, IsBuiltin = true },
            new() { Id = "A3", Label = "AI Explain Conflict Only", Category = "ai", CommandOrHint = "Read-only explanation of conflict for human reviewer", Risk = "none", Reversible = true, IsBuiltin = true },
            new() { Id = "A4", Label = "AI Pick Side with Rationale", Category = "ai", CommandOrHint = "LLM selects ours or theirs with audit explanation", Risk = "medium", Reversible = true, IsBuiltin = true },

            // Terminal Techniques (H1–H4)
            new() { Id = "H1", Label = "Force Human 3-Way Review", Category = "terminal", CommandOrHint = "Open in Syncro IDE 3-way conflict editor", Risk = "none", Reversible = true, IsBuiltin = true },
            new() { Id = "H2", Label = "Abort Operation (--abort)", Category = "terminal", CommandOrHint = "git merge/rebase/cherry-pick --abort", Risk = "none", Reversible = true, IsBuiltin = true },
            new() { Id = "H3", Label = "Skip Commit (--skip)", Category = "terminal", CommandOrHint = "git rebase/cherry-pick --skip", Risk = "medium", Reversible = true, IsBuiltin = true },
            new() { Id = "H4", Label = "Park on Conflict Branch", Category = "terminal", CommandOrHint = "Commit state to syncro/conflict/<id>", Risk = "low", Reversible = true, IsBuiltin = true }
        };

        public static List<GateDef> DefaultGates() => new()
        {
            new() { Id = "no-markers", Label = "No Conflict Markers Left (<<<<<<<)", Required = true, Skippable = false, TimeoutSec = 10 },
            new() { Id = "ast-sanity", Label = "AST Syntax Parse Sanity", Required = true, Skippable = false, TimeoutSec = 30 },
            new() { Id = "build", Label = "Build Verification (IBuildEngine)", Required = true, Skippable = false, TimeoutSec = 180 },
            new() { Id = "tests", Label = "Test Suite Verification (ITestEngine)", Required = true, Skippable = true, TimeoutSec = 300 },
            new() { Id = "contract", Label = "Cross-Project API Contract Check", Required = true, Skippable = false, TimeoutSec = 60 },
            new() { Id = "sast", Label = "Security & Secret Scan (SecurityScanner)", Required = true, Skippable = false, TimeoutSec = 45 },
            new() { Id = "lint", Label = "Code Formatter / Linter Check", Required = false, Skippable = true, TimeoutSec = 60 }
        };

        public static List<ResolutionRule> DefaultRules() => new()
        {
            new() { Order = 1, Id = "rule-secrets", Name = "secrets/env (.env*, *.pem)", ConflictTypes = new() { "secrets/env" }, GitCodes = new() { "UU", "AA", "DU", "UD" }, Chain = new(), VerifyGates = new() { "sast" }, ElseAction = "H1", ApprovalMode = "Locked Floor", Final = true },
            new() { Order = 2, Id = "rule-whitespace", Name = "whitespace-only", ConflictTypes = new() { "whitespace-only" }, GitCodes = new() { "UU" }, Chain = new() { "T3" }, VerifyGates = new() { "no-markers" }, ElseAction = "H1", ApprovalMode = "Auto Bypass" },
            new() { Order = 3, Id = "rule-lockfile", Name = "lockfile (package-lock.json, pubspec.lock)", ConflictTypes = new() { "lockfile" }, GitCodes = new() { "UU", "AA" }, Chain = new() { "S1" }, VerifyGates = new() { "build" }, ElseAction = "H1", ApprovalMode = "Auto Bypass" },
            new() { Order = 4, Id = "rule-generated", Name = "generated (*.g.dart, *.Designer.cs)", ConflictTypes = new() { "generated" }, GitCodes = new() { "UU", "AA" }, Chain = new() { "S2", "T8" }, VerifyGates = new() { "build" }, ElseAction = "H1", ApprovalMode = "Auto Bypass" },
            new() { Order = 5, Id = "rule-changelog", Name = "changelog (CHANGELOG.md, RELEASE_NOTES*)", ConflictTypes = new() { "changelog" }, GitCodes = new() { "UU" }, Chain = new() { "T10" }, VerifyGates = new() { "no-markers" }, ElseAction = "H1", ApprovalMode = "Auto Bypass" },
            new() { Order = 6, Id = "rule-imports", Name = "imports (using / import lines only)", ConflictTypes = new() { "imports" }, GitCodes = new() { "UU" }, Chain = new() { "S4", "A1" }, VerifyGates = new() { "build" }, ElseAction = "H1", ApprovalMode = "Auto Bypass" },
            new() { Order = 7, Id = "rule-version", Name = "version-bump (semver)", ConflictTypes = new() { "version-bump" }, GitCodes = new() { "UU" }, Chain = new() { "S5" }, VerifyGates = new() { "no-markers" }, ElseAction = "H1", ApprovalMode = "Auto + Notify" },
            new() { Order = 8, Id = "rule-manifest", Name = "manifest-deps (*.csproj, package.json)", ConflictTypes = new() { "manifest-deps" }, GitCodes = new() { "UU" }, Chain = new() { "S3", "S5", "S1" }, VerifyGates = new() { "build" }, ElseAction = "H1", ApprovalMode = "Auto + Notify" },
            new() { Order = 9, Id = "rule-config", Name = "config-json-yaml (appsettings*.json)", ConflictTypes = new() { "config-json-yaml" }, GitCodes = new() { "UU" }, Chain = new() { "S3", "A1" }, VerifyGates = new() { "no-markers", "sast" }, ElseAction = "H1", ApprovalMode = "Ask" },
            new() { Order = 10, Id = "rule-migration", Name = "db-migration (Migrations/*)", ConflictTypes = new() { "db-migration" }, GitCodes = new() { "UU", "AA" }, Chain = new() { "S8" }, VerifyGates = new() { "build" }, ElseAction = "H1", ApprovalMode = "Ask" },
            new() { Order = 11, Id = "rule-contract", Name = "api-contract (Controllers, OpenAPI)", ConflictTypes = new() { "api-contract" }, GitCodes = new() { "UU" }, Chain = new() { "S6", "A1" }, VerifyGates = new() { "contract", "build" }, ElseAction = "H1", MinConfidence = 0.85, ApprovalMode = "Ask" },
            new() { Order = 12, Id = "rule-infra", Name = "infra (Dockerfile, *.tf, CI yaml)", ConflictTypes = new() { "infra" }, GitCodes = new() { "UU" }, Chain = new() { "S3" }, VerifyGates = new() { "sast" }, ElseAction = "H1", ApprovalMode = "Ask" },
            new() { Order = 13, Id = "rule-binary", Name = "binary-asset (images, fonts)", ConflictTypes = new() { "binary-asset" }, GitCodes = new() { "BIN", "UU" }, Chain = new() { "A4" }, VerifyGates = new(), ElseAction = "H1", ApprovalMode = "Ask" },
            new() { Order = 14, Id = "rule-code-logic", Name = "code-logic (UU · *.cs, *.ts, *.dart)", ConflictTypes = new() { "code-logic" }, GitCodes = new() { "UU" }, Chain = new() { "T2", "S7", "S6", "A1" }, VerifyGates = new() { "build", "tests" }, ElseAction = "H1", MinConfidence = 0.85, ApprovalMode = "Auto + Notify" },
            new() { Order = 15, Id = "rule-catchall", Name = "* (catch-all fallback)", ConflictTypes = new() { "*" }, GitCodes = new() { "*" }, Chain = new() { "T2", "A1" }, VerifyGates = new() { "build" }, ElseAction = "H1", MinConfidence = 0.90, ApprovalMode = "Ask" }
        };
    }

    public sealed class ConflictTypeDef
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public List<string> Globs { get; set; } = new();
        public string? ContentRegex { get; set; }
        public string? AstNodeKind { get; set; }
        public int Priority { get; set; } = 50;
        public string Severity { get; set; } = "medium"; // low | medium | high | critical
        public string Description { get; set; } = "";
        public bool IsBuiltin { get; set; }
    }

    public sealed class TechniqueDef
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Category { get; set; } = "custom"; // git-native | structural | ai | terminal | custom
        public string CommandOrHint { get; set; } = "";
        public string Risk { get; set; } = "low";        // none | low | medium | high
        public bool Reversible { get; set; } = true;
        public int TimeoutSec { get; set; } = 120;
        public bool IsBuiltin { get; set; }
    }

    public sealed class ResolutionRule
    {
        public int Order { get; set; } = 1;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> ConflictTypes { get; set; } = new();
        public List<string> GitCodes { get; set; } = new() { "UU" };
        public List<string> Branches { get; set; } = new() { "*" };
        public List<string> Operations { get; set; } = new() { "merge", "rebase", "cherry-pick", "pull" };
        public List<string> Chain { get; set; } = new();
        public List<string> VerifyGates { get; set; } = new() { "build" };
        public string ElseAction { get; set; } = "H1";
        public double MinConfidence { get; set; } = 0.85;
        public string ApprovalMode { get; set; } = "Auto + Notify"; // Locked Floor | Auto Bypass | Auto + Notify | Ask
        public bool Final { get; set; }
        public bool ShadowMode { get; set; }
    }

    public sealed class GateDef
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public bool Required { get; set; } = true;
        public bool Skippable { get; set; }
        public int TimeoutSec { get; set; } = 120;
        public string? CustomCommand { get; set; }
    }

    /// <summary>Cherry-Pick &amp; Multi-Project Backport configuration (agentsettings.md §C.9).</summary>
    public sealed class CherryPickSettings
    {
        public bool Traceable { get; set; } = true;                  // C1: git cherry-pick -x
        public bool DedupeByPatchId { get; set; } = true;            // C8: git cherry -v
        public bool RevertOfRevertGuard { get; set; } = true;        // C11: detect Revert pairs
        public bool ThreeWayBlobFallback { get; set; } = true;       // -3 / --threeway
        public string EmptyHandling { get; set; } = "drop";          // C7: drop | keep | stop
        public int MainlineParent { get; set; } = 1;                 // C3: -m 1
        public string CommitOrder { get; set; } = "topological";     // topological | author-date
        public string OnConflict { get; set; } = "resolution-rules"; // resolution-rules | ask-human | abort
        public string MultiCommitFailure { get; set; } = "stop";     // stop | skip-and-log
        public List<string> BackportTargets { get; set; } = new() { "release/*", "main" };
        public string BackportMode { get; set; } = "branch-pr";      // branch-pr | direct-if-unprotected
        public bool MultiProjectGroupPick { get; set; } = true;      // Producers First -> Verify Consumer Contracts
    }

    /// <summary>Persisted form of the old in-memory <c>GitSyncPolicy</c> registrations.</summary>
    public sealed class GitSyncSettings
    {
        public int IntervalMinutes { get; set; } = 2;
        public string PullStrategy { get; set; } = "ff-only";        // ff-only | rebase | merge
        public bool AutoStash { get; set; } = true;
        public string ConflictStyle { get; set; } = "zdiff3";        // merge | diff3 | zdiff3
        public bool RerereEnabled { get; set; } = true;
        public bool RerereAutoUpdate { get; set; } = true;
        public List<GitSyncRepo> Repos { get; set; } = new();
    }

    public sealed class GitSyncRepo
    {
        public string Repo { get; set; } = "";
        public bool AutoFetch { get; set; } = true;
        public bool AutoCommit { get; set; }
        public bool AutoPush { get; set; }
        public string AutoCommitMessage { get; set; } = "chore: auto-sync";
        public List<string> BranchAllowlist { get; set; } = new();
    }

    public sealed class CommitStyleSettings
    {
        public bool ConventionalCommits { get; set; } = true;
        public int MaxSubjectLength { get; set; } = 72;
        public string Template { get; set; } = "{type}({scope}): {summary}";
        public List<string> Trailers { get; set; } = new() { "Resolved-by: Syncro-Agent", "Syncro-Rule: {ruleId}" };
        public string SigningMode { get; set; } = "none";            // none | gpg | ssh
    }

    public sealed class PrSettings
    {
        public string DefaultBaseBranch { get; set; } = "main";
        public bool OpenAsDraft { get; set; }
        public bool ReadGithubBranchProtection { get; set; } = true;
        public List<string> DefaultReviewers { get; set; } = new();
        public List<string> DefaultLabels { get; set; } = new() { "syncro-agent", "sre-verified" };
        public string BodyTemplate { get; set; } = "## Summary\n{summary}\n\n## Verification Gates\n- Build: {buildStatus}\n- Tests: {testStatus}\n- Contract Check: {contractStatus}";
    }

    public sealed class AutomationSettings
    {
        public int ShadowRunsForNewRules { get; set; } = 5;
        public bool TriggerOnConflict { get; set; } = true;
        public bool TriggerOnContractDrift { get; set; } = true;
        public bool TriggerOnBuildFail { get; set; }
        public bool LearningSuggestOnly { get; set; } = true;
        public int SuggestAfterApprovals { get; set; } = 5;
    }
}

namespace Syncro.Desktop.Services.Agent.Policy
{
    /// <summary>
    /// This PC as a platform agent (platform-desktop-plan.md Phase 2): registers with the web
    /// platform, sends heartbeats and runs jobs queued for the signed-in member, such as setting up
    /// the workspace for a newly assigned task.
    /// </summary>
    public sealed class PlatformAgentSettings
    {
        /// <summary>Register this PC and poll for jobs while signed in.</summary>
        public bool Enabled { get; set; } = true;
        /// <summary>Shown to admins in the Agents tab. Empty = Windows machine name.</summary>
        public string DeviceName { get; set; } = "";
        /// <summary>Where task repos are cloned. Empty = Documents\SyncroWorkspaces.</summary>
        public string WorkspaceRoot { get; set; } = "";
        /// <summary>
        /// Off (default): each setup job waits for approval in the inbox. On: setup jobs for your own
        /// tasks run on their own (still subject to every hard floor and permission list).
        /// </summary>
        public bool AutoAcceptSetupJobs { get; set; }
    }
}
