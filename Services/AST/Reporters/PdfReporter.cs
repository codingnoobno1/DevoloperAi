using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;
using Colors = QuestPDF.Helpers.Colors;
using OxyPlot.SkiaSharp;
using Syncro.Desktop.Services.AST.Models;
using Syncro.Desktop.Services.AST.Graph;

namespace Syncro.Desktop.Services.AST.Reporters;

public class PdfReporter : IAstReporter
{
    private readonly GraphExporter _graphExporter;

    public PdfReporter(GraphExporter graphExporter)
    {
        _graphExporter = graphExporter;
        
        // QuestPDF licensing configuration for community open source usage
        try
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }
        catch
        {
            // Suppress if license already configured elsewhere
        }
    }

    public async Task<string> ExportAsync(AstProjectMap map, string outputPath)
    {
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }

        string fullPath = Path.Combine(outputPath, $"{map.ProjectName}_ast_report.pdf");

        // 1. Generate the distribution chart bytes using OxyPlot SkiaSharp
        byte[] chartBytes = Array.Empty<byte>();
        try
        {
            // Re-create the AstGraph dynamically for visualization stats
            var graph = new AstGraph();
            foreach (var node in map.Nodes)
            {
                graph.AddNode(node);
            }
            // Add edges from parsed data to populate chart categories
            foreach (var node in map.Nodes.Where(n => n.Type == AstNodeType.Module && n.Id.Contains("::import::")))
            {
                graph.AddEdge(node.FilePath, node.Name, "imports");
            }

            var plotModel = _graphExporter.ToOxyPlotModel(graph, "Code Entity Distributions");
            var pngExporter = new PngExporter { Width = 600, Height = 350 };
            using var chartStream = new MemoryStream();
            pngExporter.Export(plotModel, chartStream);
            chartBytes = chartStream.ToArray();
        }
        catch
        {
            // Suppress chart rendering errors
        }

        // 2. Build PDF using QuestPDF DSL
        await Task.Run(() =>
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Arial"));

                    // Cover Header
                    page.Header().Column(col =>
                    {
                        col.Item().Text($"SYNCRO AST ANALYSIS REPORT").FontSize(22).Bold().FontColor(Colors.Blue.Darken2);
                        col.Item().Text($"Project: {map.ProjectName}").FontSize(14).Italic();
                        col.Item().LineHorizontal(1f).LineColor(Colors.Grey.Lighten1);
                        col.Spacing(5);
                    });

                    // Report Body
                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                    {
                        // Summary Table
                        col.Item().Text("1. Executive Summary").FontSize(16).Bold().FontColor(Colors.Blue.Darken1);
                        col.Item().PaddingTop(5).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(120);
                                columns.RelativeColumn();
                            });
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Language").Bold();
                            table.Cell().Padding(5).Text(map.Language);
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Framework").Bold();
                            table.Cell().Padding(5).Text(map.Framework);
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Endpoints Scanned").Bold();
                            table.Cell().Padding(5).Text(map.Endpoints.Count.ToString());
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("DTOs Resolved").Bold();
                            table.Cell().Padding(5).Text(map.Dtos.Count.ToString());
                            
                            table.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Open Ports").Bold();
                            table.Cell().Padding(5).Text(map.Ports.Count(p => p.State == "Open").ToString());
                        });

                        col.Item().PageBreak();

                        // Visual Charts Section
                        if (chartBytes.Length > 0)
                        {
                            col.Item().Text("2. Architectural Composition").FontSize(16).Bold().FontColor(Colors.Blue.Darken1);
                            col.Item().PaddingTop(10).Image(chartBytes);
                            col.Item().PageBreak();
                        }

                        // API Endpoints Section
                        col.Item().Text("3. REST API Endpoints").FontSize(16).Bold().FontColor(Colors.Blue.Darken1);
                        if (map.Endpoints.Count == 0)
                        {
                            col.Item().PaddingTop(5).Text("No endpoints discovered in source files or Swagger specs.");
                        }
                        else
                        {
                            col.Item().PaddingTop(5).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(60);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(120);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text("Method").Bold();
                                    header.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text("Path").Bold();
                                    header.Cell().Background(Colors.Blue.Lighten4).Padding(5).Text("Handler File").Bold();
                                });

                                foreach (var ep in map.Endpoints)
                                {
                                    table.Cell().Padding(5).Text(ep.Method).Bold().FontColor(
                                        ep.Method == "GET" ? Colors.Green.Darken2 : 
                                        ep.Method == "POST" ? Colors.Blue.Darken2 : Colors.Red.Darken2
                                    );
                                    table.Cell().Padding(5).Text(ep.Path);
                                    table.Cell().Padding(5).Text(Path.GetFileName(ep.FilePath)).FontSize(9);
                                }
                            });
                        }

                        col.Item().PageBreak();

                        // DTO Schema Models Section
                        col.Item().Text("4. DTO Schema Definitions").FontSize(16).Bold().FontColor(Colors.Blue.Darken1);
                        if (map.Dtos.Count == 0)
                        {
                            col.Item().PaddingTop(5).Text("No Data Transfer Objects mapped in directories.");
                        }
                        else
                        {
                            foreach (var dto in map.Dtos)
                            {
                                col.Item().PaddingTop(8).Column(dtoCol =>
                                {
                                    dtoCol.Item().Text($"Class/Interface: {dto.Name}").Bold().FontSize(12).FontColor(Colors.Grey.Darken3);
                                    dtoCol.Item().PaddingLeft(10).Table(dtoTable =>
                                    {
                                        dtoTable.ColumnsDefinition(cols =>
                                        {
                                            cols.RelativeColumn();
                                            cols.RelativeColumn();
                                        });

                                        dtoTable.Header(hdr =>
                                        {
                                            hdr.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Property").Bold();
                                            hdr.Cell().Background(Colors.Grey.Lighten2).Padding(3).Text("Type").Bold();
                                        });

                                        foreach (var prop in dto.Properties)
                                        {
                                            dtoTable.Cell().Padding(3).Text(prop.Key);
                                            dtoTable.Cell().Padding(3).Text(prop.Value).Italic();
                                        }
                                    });
                                });
                            }
                        }
                    });

                    // Report Footer
                    page.Footer().Column(col =>
                    {
                        col.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"Generated by Syncro.Desktop").FontSize(9).Italic();
                            row.RelativeItem().AlignRight().Text(t =>
                            {
                                t.Span("Page ");
                                t.CurrentPageNumber();
                                t.Span(" of ");
                                t.TotalPages();
                            });
                        });
                    });
                });
            }).GeneratePdf(fullPath);
        });

        return fullPath;
    }
}
