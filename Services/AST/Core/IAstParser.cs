using System.Collections.Generic;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.AST.Core;

public interface IAstParser
{
    // File extensions this parser handles, e.g., [".cs"] or [".ts", ".tsx"]
    IReadOnlyList<string> SupportedExtensions { get; }

    // Parse a single file, extracting nodes (classes, methods, endpoints)
    Task<IReadOnlyList<AstNode>> ParseFileAsync(string filePath, AstContext ctx);

    // Parse an entire project directory using language-specific workspace capabilities
    Task<IReadOnlyList<AstNode>> ParseProjectAsync(string rootPath, AstContext ctx);
}
