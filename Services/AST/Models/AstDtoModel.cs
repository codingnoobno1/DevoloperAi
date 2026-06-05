using System.Collections.Generic;

namespace Syncro.Desktop.Services.AST.Models;

public class AstDtoModel
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int LineNumber { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new(); // PropertyName -> Type
    public List<string> Annotations { get; set; } = new(); // e.g. [Required], [EmailAddress]
    public string? ParentClass { get; set; }
}
