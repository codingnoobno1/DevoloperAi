using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Nlp;

/// <summary>
/// The DB-based error→action table (<c>mappings.jsonl</c>). "Learning" = inserting rows
/// (from the LLM teacher / humans) and bumping counters from outcomes. No model, no training.
/// </summary>
public class ErrorMappingStore
{
    private readonly SyncroDb _db;
    private readonly KeywordLexicon _lexicon;

    public ErrorMappingStore(SyncroDb db, KeywordLexicon lexicon)
    {
        _db = db;
        _lexicon = lexicon;
    }

    public async Task<List<ErrorMapping>> AllAsync()
    {
        await EnsureSeedAsync();
        return await _db.ReadJsonlAsync<ErrorMapping>(_db.MappingsPath);
    }

    /// <summary>Insert a new row or replace an existing one by id.</summary>
    public async Task UpsertAsync(ErrorMapping row)
    {
        var all = await _db.ReadJsonlAsync<ErrorMapping>(_db.MappingsPath);
        row.UpdatedAt = DateTime.UtcNow;
        var idx = all.FindIndex(r => r.Id == row.Id);
        if (idx >= 0) all[idx] = row; else all.Add(row);
        await _db.RewriteJsonlAsync(_db.MappingsPath, all);
    }

    /// <summary>Outcome feedback: hits++, and worked++/fails++ — the only "learning" step.</summary>
    public async Task BumpAsync(string mappingId, bool worked)
    {
        var all = await _db.ReadJsonlAsync<ErrorMapping>(_db.MappingsPath);
        var row = all.Find(r => r.Id == mappingId);
        if (row == null) return;
        row.Hits++;
        if (worked) row.Worked++; else row.Fails++;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.RewriteJsonlAsync(_db.MappingsPath, all);
    }

    private async Task EnsureSeedAsync()
    {
        if (File.Exists(_db.MappingsPath)) return;
        var seed = new List<ErrorMapping>();
        foreach (var entry in await _lexicon.EntriesAsync())
        {
            if (seed.Exists(m => m.Keywords.Contains(entry.Signal))) continue;
            seed.Add(new ErrorMapping
            {
                Keywords = { entry.Signal },
                Label = entry.Label,
                Solution = entry.Label,
                Source = "seed"
            });
        }
        await _db.RewriteJsonlAsync(_db.MappingsPath, seed);
    }
}
