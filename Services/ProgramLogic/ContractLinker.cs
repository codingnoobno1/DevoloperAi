using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class ContractLinker
    {
        public async Task LinkAsync(string frontendFeaturePath, string backendFeaturePath)
        {
            try
            {
                // Simple heuristic to extract the base URL from the generated backend controller
                string backendRoute = "/api/default";
                var controllerFiles = Directory.GetFiles(backendFeaturePath, "*.cs");

                foreach (var file in controllerFiles)
                {
                    var content = await File.ReadAllTextAsync(file);
                    // Match [Route("api/[controller]")] or similar
                    var match = Regex.Match(content, @"\[Route\(""(?<route>[^""]+)""\)\]");
                    if (match.Success)
                    {
                        backendRoute = "/" + match.Groups["route"].Value.Replace("[controller]", "feature").ToLower();
                        break;
                    }
                }

                // Update the dart repository file
                var dartFiles = Directory.GetFiles(frontendFeaturePath, "*.dart");
                foreach (var file in dartFiles)
                {
                    if (file.Contains("repository"))
                    {
                        var content = await File.ReadAllTextAsync(file);
                        
                        // Attempt to inject the linked baseUrl
                        if (!content.Contains("baseUrl"))
                        {
                            content = content.Replace("class ", $"const String baseUrl = '{backendRoute}';\n\nclass ");
                            await File.WriteAllTextAsync(file, content);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ContractLinker] Failed to link contracts: {ex.Message}");
            }
        }
    }
}
