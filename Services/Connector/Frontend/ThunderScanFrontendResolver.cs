using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Frontend
{
    /// <summary>
    /// AST-first <see cref="IFrontendContractResolver"/>. When a Reverse-AST scan
    /// (<c>thunder scan</c>) has already run against a Flutter workspace, its output at
    /// <c>&lt;path&gt;/.thunder/reverse/connector/api.json</c> carries real parser-derived call sites
    /// (verb, endpoint template, enclosing service/method, owning server) — strictly better than the
    /// regex heuristics. This decorator prefers that artifact when present and delegates to the
    /// heuristic <see cref="FrontendContractResolver"/> otherwise, so behavior is unchanged for any
    /// workspace that hasn't been scanned.
    /// </summary>
    public sealed class ThunderScanFrontendResolver : IFrontendContractResolver
    {
        private readonly IFrontendContractResolver _inner;

        /// <summary>Relative location of the Reverse-AST connector output under a scanned workspace.</summary>
        public const string ApiJsonRelativePath = ".thunder/reverse/connector/api.json";

        public ThunderScanFrontendResolver(IFrontendContractResolver inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public Task<FrontendContract> ResolveAsync(string frontendPath, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(frontendPath))
            {
                var apiJson = Path.Combine(frontendPath, ".thunder", "reverse", "connector", "api.json");
                if (File.Exists(apiJson))
                {
                    var contract = TryLoadScan(frontendPath, apiJson);
                    if (contract != null)
                        return Task.FromResult(contract);
                }
            }

            return _inner.ResolveAsync(frontendPath, ct);
        }

        /// <summary>
        /// Parses <c>connector/api.json</c> into a <see cref="FrontendContract"/>. Returns null on any
        /// read/parse failure or when the scan yielded zero calls, so the caller falls back to heuristics.
        /// </summary>
        private static FrontendContract? TryLoadScan(string frontendPath, string apiJsonPath)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(apiJsonPath));
                if (!doc.RootElement.TryGetProperty("calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
                    return null;

                var contract = new FrontendContract
                {
                    FrontendPath = frontendPath,
                    Stack = FrontendStack.Flutter
                };

                foreach (var call in calls.EnumerateArray())
                {
                    var endpoint = GetString(call, "endpoint");
                    if (string.IsNullOrWhiteSpace(endpoint)) continue;

                    var site = new ApiCallSite
                    {
                        Method = GetString(call, "method").ToHttpVerb(),
                        PathTemplate = PathNormalizer.NormalizeCallPath(endpoint),
                        Client = GetString(call, "client"),
                    };

                    if (call.TryGetProperty("provenance", out var prov) && prov.ValueKind == JsonValueKind.Object)
                    {
                        var file = GetString(prov, "file");
                        site.FilePath = string.IsNullOrWhiteSpace(file) ? null : Path.Combine(frontendPath, file);
                        if (prov.TryGetProperty("line", out var line) && line.TryGetInt32(out var l))
                            site.Line = l;
                    }

                    contract.Calls.Add(site);
                }

                if (contract.Calls.Count == 0)
                    return null;

                contract.Calls = FrontendApiExtractors.Dedupe(contract.Calls);
                contract.Notes.Add($"Loaded {contract.Calls.Count} AST-accurate call(s) from Reverse-AST scan ({ApiJsonRelativePath}).");
                return contract;
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(JsonElement obj, string prop) =>
            obj.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
    }
}
