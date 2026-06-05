namespace Syncro.Desktop.Services.AST.Models;

public class PortInfo
{
    public int Port { get; set; }
    public string State { get; set; } = "Unknown"; // e.g. Open, Closed, Filtered
    public string Service { get; set; } = "Unknown"; // e.g. HTTP, PostgreSQL, Redis
    public string Type { get; set; } = "Scanned"; // Scanned or Declared (in config)
}
