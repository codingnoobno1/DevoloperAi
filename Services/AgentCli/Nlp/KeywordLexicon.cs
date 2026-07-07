using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.AgentCli.Nlp;

/// <summary>Compiled lexicon entry: a regex over (raw or normalized) error text → signal + seed label.</summary>
public record LexEntry(Regex Pattern, string Signal, string Label);

/// <summary>Raw lexicon value as stored in error-keywords.json.</summary>
public class LexValue
{
    public string Signal { get; set; } = "";
    public string Label { get; set; } = "";
}

/// <summary>
/// Loads the curated phrase→signal lexicon from <c>error-keywords.json</c>. Seeds a default
/// lexicon on first run so keyword extraction works offline, day one. Extend by editing the JSON.
/// </summary>
public class KeywordLexicon
{
    private readonly SyncroDb _db;
    private List<LexEntry>? _cache;

    public KeywordLexicon(SyncroDb db) => _db = db;

    public async Task<IReadOnlyList<LexEntry>> EntriesAsync()
    {
        if (_cache != null) return _cache;
        await EnsureSeedAsync();
        var dict = await _db.ReadJsonAsync<Dictionary<string, LexValue>>(_db.LexiconPath) ?? new();
        var list = new List<LexEntry>();
        foreach (var (pattern, value) in dict)
            list.Add(new LexEntry(
                new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                value.Signal, value.Label));
        _cache = list;
        return list;
    }

    private async Task EnsureSeedAsync()
    {
        if (File.Exists(_db.LexiconPath)) return;
        var seed = new Dictionary<string, LexValue>
        {
            ["cannot find module|modulenotfounderror|err_module_not_found|no module named"] =
                new() { Signal = "missing-module", Label = "install_dep" },
            ["eaddrinuse|address already in use|port .* (?:already )?in use"] =
                new() { Signal = "port-in-use", Label = "change_port" },
            ["cs0246|cs0103|are you missing a using"] =
                new() { Signal = "missing-using", Label = "add_using" },
            ["ts2307|cannot find name|ts2304"] =
                new() { Signal = "ts-missing", Label = "install_dep" },
            ["enoent|no such file or directory"] =
                new() { Signal = "missing-file", Label = "create_file" },
            ["permission denied|eacces|access is denied"] =
                new() { Signal = "permission", Label = "human" }
        };
        await _db.WriteJsonAtomicAsync(_db.LexiconPath, seed);
    }
}
