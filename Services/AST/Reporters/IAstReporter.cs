using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Models;

namespace Syncro.Desktop.Services.AST.Reporters;

public interface IAstReporter
{
    Task<string> ExportAsync(AstProjectMap map, string outputPath);
}
