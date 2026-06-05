using System;
using System.Collections.Generic;
using System.Linq;

namespace Syncro.Desktop.Services.AST.Core;

public class AstRegistry
{
    private readonly Dictionary<string, IAstParser> _parsers = new(StringComparer.OrdinalIgnoreCase);

    public AstRegistry(IEnumerable<IAstParser> parsers)
    {
        foreach (var parser in parsers)
        {
            foreach (var ext in parser.SupportedExtensions)
            {
                _parsers[ext.ToLower()] = parser;
            }
        }
    }

    // Resolves appropriate parser for a given file extension (e.g. ".cs")
    public IAstParser? GetParser(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension)) return null;
        
        string normalized = fileExtension.StartsWith(".") ? fileExtension : $".{fileExtension}";
        _parsers.TryGetValue(normalized.ToLower(), out var parser);
        return parser;
    }

    // List all registered file extensions
    public IReadOnlyList<string> RegisteredExtensions => _parsers.Keys.ToList().AsReadOnly();
}
