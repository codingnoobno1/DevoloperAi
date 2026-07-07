using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Syncro.Desktop.Services.Connector.Models;

namespace Syncro.Desktop.Services.Connector.Storage
{
    /// <summary>Default <see cref="IConnectorProjectStore"/>. connector.json is the source of
    /// truth; connector.md is regenerated from it and never hand-edited.</summary>
    public sealed class ConnectorProjectStore : IConnectorProjectStore
    {
        private const string JsonFileName = "connector.json";
        private const string MarkdownFileName = "connector.md";

        public async Task<ConnectorProject?> LoadAsync(string projectRoot, CancellationToken ct = default)
        {
            string path = Path.Combine(projectRoot, JsonFileName);
            if (!File.Exists(path))
                return null;

            string json = await File.ReadAllTextAsync(path, ct);
            return JsonConvert.DeserializeObject<ConnectorProject>(json);
        }

        public async Task SaveAsync(string projectRoot, ConnectorProject project, CancellationToken ct = default)
        {
            Directory.CreateDirectory(projectRoot);
            string path = Path.Combine(projectRoot, JsonFileName);
            string json = JsonConvert.SerializeObject(project, Formatting.Indented);
            await File.WriteAllTextAsync(path, json, ct);
        }

        public async Task<ConnectorProject> UpsertBackendAsync(
            string projectRoot, BackendContract contract, string? runCommand = null, int? port = null, CancellationToken ct = default)
        {
            var project = await LoadAsync(projectRoot, ct) ?? new ConnectorProject();

            project.Backend = new BackendConfig
            {
                Path = contract.BackendPath,
                Stack = contract.Stack.ToString(),
                RunCommand = runCommand,
                Port = port,
                SwaggerPath = contract.SwaggerPath
            };

            await SaveAsync(projectRoot, project, ct);
            await WriteMarkdownSummaryAsync(projectRoot, project, ct);
            return project;
        }

        public async Task WriteMarkdownSummaryAsync(string projectRoot, ConnectorProject project, CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Connector Summary");
            sb.AppendLine();
            sb.AppendLine("> Generated from connector.json — do not hand-edit, changes will be overwritten.");
            sb.AppendLine();

            sb.AppendLine("## Frontend");
            if (project.Frontend == null)
                sb.AppendLine("_Not connected yet._");
            else
                sb.AppendLine($"- Stack: {project.Frontend.Stack}\n- Path: {project.Frontend.Path}\n- Run: {project.Frontend.RunCommand}");

            sb.AppendLine();
            sb.AppendLine("## Backend");
            if (project.Backend == null)
            {
                sb.AppendLine("_Not connected yet._");
            }
            else
            {
                sb.AppendLine($"- Stack: {project.Backend.Stack}");
                sb.AppendLine($"- Path: {project.Backend.Path}");
                if (project.Backend.Port.HasValue) sb.AppendLine($"- Port: {project.Backend.Port}");
                if (!string.IsNullOrWhiteSpace(project.Backend.RunCommand)) sb.AppendLine($"- Run: {project.Backend.RunCommand}");
                if (!string.IsNullOrWhiteSpace(project.Backend.SwaggerPath)) sb.AppendLine($"- Swagger: {project.Backend.SwaggerPath}");
            }

            sb.AppendLine();
            sb.AppendLine("## Mappings");
            if (project.Mappings.Count == 0)
            {
                sb.AppendLine("_No frontend↔backend mappings yet — requires the frontend extractor._");
            }
            else
            {
                sb.AppendLine("| Call | Route | State |");
                sb.AppendLine("|---|---|---|");
                foreach (var m in project.Mappings)
                    sb.AppendLine($"| {m.Call} | {m.Route ?? "—"} | {m.State} |");
            }

            Directory.CreateDirectory(projectRoot);
            string path = Path.Combine(projectRoot, MarkdownFileName);
            await File.WriteAllTextAsync(path, sb.ToString(), ct);
        }
    }
}
