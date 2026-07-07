using System;
using System.Collections.Generic;
using System.Linq;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Analyzers;

public class DtoAnalyzer
{
    public List<AstDtoModel> Analyze(IEnumerable<AstNode> nodes)
    {
        var dtos = new List<AstDtoModel>();
        var nodeList = nodes.ToList();

        foreach (var node in nodeList)
        {
            // Identify DTO classes by suffix, explicitly tagged types, or conventional folders
            string pathLower = node.FilePath.Replace('\\', '/').ToLower();
            bool isConventionalFolder = pathLower.Contains("/models/") || 
                                         pathLower.Contains("/dto/") || 
                                         pathLower.Contains("/dtos/") || 
                                         pathLower.Contains("/entities/") || 
                                         pathLower.Contains("/entity/");

            if (node.Type == AstNodeType.Dto || 
                node.Name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase) || 
                node.Name.EndsWith("Model", StringComparison.OrdinalIgnoreCase) ||
                isConventionalFolder)
            {
                if (node.Type == AstNodeType.Class || node.Type == AstNodeType.Interface || node.Type == AstNodeType.Dto)
                {
                    if (!dtos.Any(d => d.Name == node.Name && d.FilePath == node.FilePath))
                    {
                        var dto = new AstDtoModel
                        {
                            Name = node.Name,
                            FilePath = node.FilePath,
                            LineNumber = node.LineNumber
                        };
                        
                        // Parse inheritance if documented in metadata
                        if (node.Metadata.TryGetValue("parentClass", out var p))
                        {
                            dto.ParentClass = p.ToString();
                        }

                        // Match property nodes: nodes belonging to this class/interface
                        var childProps = nodeList.Where(n => n.FilePath == node.FilePath && 
                                                             n.Type == AstNodeType.Property && 
                                                             n.Id.StartsWith(node.Id + "::"));
                        foreach (var prop in childProps)
                        {
                            dto.Properties[prop.Name] = prop.ReturnType ?? "object";
                            
                            // Capture property attributes
                            if (prop.Metadata.TryGetValue("annotations", out var annObj) && annObj is List<string> annotationsList)
                            {
                                foreach (var ann in annotationsList)
                                {
                                    if (!dto.Annotations.Contains(ann)) dto.Annotations.Add(ann);
                                }
                            }
                        }

                        dtos.Add(dto);
                    }
                }
            }
        }

        return dtos;
    }
}
