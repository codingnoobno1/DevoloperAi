using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Analyzers;

public class ComplexityAnalyzer
{
    public int CalculateComplexity(string code)
    {
        if (string.IsNullOrEmpty(code)) return 1;

        int complexity = 1;

        // Clean comments to avoid counting keywords inside docstrings or inline comments
        string cleanCode = RemoveComments(code);

        // Pattern matches for common branching statements across C#, JS/TS, Python
        string[] patterns = new[]
        {
            @"\bif\b",
            @"\bfor\b",
            @"\bwhile\b",
            @"\bcatch\b",
            @"\bcase\b",
            @"\b&&",
            @"\b\|\|",
            @"\b\?\?"
        };

        foreach (var pattern in patterns)
        {
            var matches = Regex.Matches(cleanCode, pattern);
            complexity += matches.Count;
        }

        return complexity;
    }

    private string RemoveComments(string code)
    {
        // Remove C#/JS style single-line // comments
        string noSingleLine = Regex.Replace(code, @"//.*", "");
        
        // Remove C#/JS style multi-line /* ... */ comments
        string noMultiLine = Regex.Replace(noSingleLine, @"/\*.*?\*/", "", RegexOptions.Singleline);
        
        // Remove Python style single-line # comments
        string noPythonComments = Regex.Replace(noMultiLine, @"#.*", "");
        
        // Remove Python style docstrings """ ... """
        string noDocstrings = Regex.Replace(noPythonComments, @"""""""(.*?)""""""", "", RegexOptions.Singleline);

        return noDocstrings;
    }

    // Run complexity analysis on a method node
    public void AnalyzeNode(AstNode node)
    {
        if (node.Type != AstNodeType.Method) return;

        try
        {
            if (File.Exists(node.FilePath))
            {
                // To accurately calculate complexity of a single method without full compilation sematics,
                // we extract the method block from the file.
                string fileContent = File.ReadAllText(node.FilePath);
                string methodBody = ExtractMethodBody(fileContent, node.Name, node.LineNumber);
                
                int complexity = CalculateComplexity(methodBody);
                node.Metadata["cyclomaticComplexity"] = complexity;
                node.Metadata["complexityRating"] = GetComplexityRating(complexity);
            }
        }
        catch
        {
            node.Metadata["cyclomaticComplexity"] = 1;
            node.Metadata["complexityRating"] = "Low";
        }
    }

    private string ExtractMethodBody(string fileContent, string methodName, int lineNumber)
    {
        string[] lines = fileContent.Split('\n');
        if (lineNumber > lines.Length) return "";

        var sb = new System.Text.StringBuilder();
        int braceCount = 0;
        bool bodyStarted = false;

        // Walk lines starting from the declaration line
        for (int i = lineNumber - 1; i < lines.Length; i++)
        {
            string line = lines[i];
            sb.AppendLine(line);

            if (line.Contains("{"))
            {
                braceCount += line.Count(c => c == '{');
                bodyStarted = true;
            }
            if (line.Contains("}"))
            {
                braceCount -= line.Count(c => c == '}');
            }

            if (bodyStarted && braceCount <= 0)
            {
                break; // Found matching closing brace, end of method
            }
        }

        return sb.ToString();
    }

    private string GetComplexityRating(int complexity)
    {
        if (complexity <= 4) return "Low";
        if (complexity <= 8) return "Moderate";
        if (complexity <= 14) return "High";
        return "Very High / Refactor Needed";
    }
}
