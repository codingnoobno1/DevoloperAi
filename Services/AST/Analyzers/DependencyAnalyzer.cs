using System;
using System.Collections.Generic;
using System.Linq;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Analyzers;

public class DependencyAnalyzer
{
    public List<AstDependencyInfo> Analyze(IEnumerable<AstNode> nodes)
    {
        var dependencies = new List<AstDependencyInfo>();

        foreach (var node in nodes)
        {
            // Detect configuration modules representing dependencies
            bool isNpmDep = node.Type == AstNodeType.Module && node.Id.Contains("::dependency::");
            bool isPipDep = node.Type == AstNodeType.Module && node.Id.Contains("::pip::");
            bool isNugetDep = node.Type == AstNodeType.Module && node.Id.Contains("::nuget::"); // or in project file parser

            if (isNpmDep || isPipDep || isNugetDep)
            {
                string type = isNpmDep ? "npm" : (isPipDep ? "pip" : "NuGet");
                string version = "Latest";

                if (node.Metadata.TryGetValue("version", out var verObj) && verObj != null)
                {
                    version = verObj.ToString() ?? "Latest";
                }

                if (!dependencies.Any(d => d.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase) && d.Type.Equals(type, StringComparison.OrdinalIgnoreCase)))
                {
                    dependencies.Add(new AstDependencyInfo
                    {
                        Name = node.Name,
                        Version = version,
                        Type = type,
                        IsTransitive = false
                    });
                }
            }
        }

        return dependencies;
    }
}
