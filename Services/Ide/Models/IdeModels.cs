using System;

namespace Syncro.Desktop.Services.Ide.Models;

public class Workspace
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string RootPath { get; set; } = "";
    public DateTime LastOpened { get; set; } = DateTime.UtcNow;
}

public class EditorTab
{
    public string Uri { get; set; } = "";        // "file:///abs/path"
    public string FilePath { get; set; } = "";
    public string Language { get; set; } = "plaintext";
    public bool IsDirty { get; set; }
    public bool IsActive { get; set; }
}

public class FileNode
{
    public string Name { get; set; } = "";
    public string FullPath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool HasChildren { get; set; }
}

public enum SidePanelView { Explorer, Search, Git, Ast, Agents }
