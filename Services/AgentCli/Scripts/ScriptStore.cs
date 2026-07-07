using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AgentCli.Models;

namespace Syncro.Desktop.Services.AgentCli.Scripts;

/// <summary>
/// Stores scripts so the same fix is reused next time instead of regenerated. Index in
/// <c>scripts.json</c>, bodies as real files under <c>bodies/</c>. Dedup by content hash;
/// counters drive reuse ranking; auto-promote to <c>safe</c> after clean runs.
/// </summary>
public class ScriptStore
{
    private readonly SyncroDb _db;
    private readonly ScriptSafetyScanner _scanner;

    public ScriptStore(SyncroDb db, ScriptSafetyScanner scanner)
    {
        _db = db;
        _scanner = scanner;
    }

    public async Task<List<ScriptRecord>> AllAsync() =>
        await _db.ReadJsonAsync<List<ScriptRecord>>(_db.ScriptsIndexPath) ?? new();

    public async Task<ScriptRecord?> GetAsync(string id) =>
        (await AllAsync()).Find(s => s.Id == id);

    /// <summary>Save a new script (dedup by hash). Returns the existing record if identical.</summary>
    public async Task<ScriptRecord> SaveAsync(ScriptDraft draft)
    {
        var all = await AllAsync();
        var hash = Hash(draft.Body);

        var existing = all.Find(s => s.Hash == hash);
        if (existing != null) return existing;

        var record = new ScriptRecord
        {
            Name = draft.Name,
            Shell = draft.Shell,
            Purpose = draft.Purpose,
            Keywords = new List<string>(draft.Keywords),
            Platform = new List<string>(draft.Platform),
            Params = new List<string>(draft.Params),
            Source = draft.Source,
            Hash = hash
        };

        var fileName = record.Id + Ext(draft.Shell);
        record.BodyPath = Path.Combine("bodies", fileName);

        var scan = _scanner.Scan(draft.Body, draft.Shell);
        record.Flagged = !scan.Safe;   // flagged scripts never auto-run

        await File.WriteAllTextAsync(_db.ScriptBodyPath(fileName), draft.Body);
        all.Add(record);
        await _db.WriteJsonAtomicAsync(_db.ScriptsIndexPath, all);
        await _db.AppendAuditAsync(new AuditEntry
        {
            Action = "script.save",
            Files = { record.BodyPath },
            Note = record.Id + (record.Flagged ? " (FLAGGED by safety scanner)" : "")
        });
        return record;
    }

    public async Task<string> ReadBodyAsync(string id)
    {
        var record = await GetAsync(id);
        if (record == null) return "";
        var fileName = Path.GetFileName(record.BodyPath);
        var path = _db.ScriptBodyPath(fileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : "";
    }

    /// <summary>Outcome feedback. Auto-promotes to safe after 3 clean runs (unless flagged).</summary>
    public async Task BumpAsync(string id, bool succeeded)
    {
        var all = await AllAsync();
        var record = all.Find(s => s.Id == id);
        if (record == null) return;

        record.Runs++;
        if (succeeded) record.Succeeded++; else record.Failed++;
        record.LastUsed = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        if (!record.Flagged && record.Succeeded >= 3 && record.Failed == 0)
            record.Safe = true;

        await _db.WriteJsonAtomicAsync(_db.ScriptsIndexPath, all);
    }

    public async Task MarkSafeAsync(string id, bool safe)
    {
        var all = await AllAsync();
        var record = all.Find(s => s.Id == id);
        if (record == null || record.Flagged) return;
        record.Safe = safe;
        record.UpdatedAt = DateTime.UtcNow;
        await _db.WriteJsonAtomicAsync(_db.ScriptsIndexPath, all);
    }

    private static string Hash(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));
        return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Ext(string shell) => shell switch
    {
        "sh" => ".sh",
        "ps1" => ".ps1",
        "py" => ".py",
        _ => ".bat"
    };
}
