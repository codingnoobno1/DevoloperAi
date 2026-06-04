using System.Threading.Tasks;

namespace Syncro.Desktop.Services.ProgramLogic
{
    public class SimpleNLPServices : INLPServices
    {
        public Task<string> DetectProjectAndAIMode(string userPrompt)
        {
            userPrompt = userPrompt.ToLower();

            if (userPrompt.Contains("mern") || userPrompt.Contains("mongo") || userPrompt.Contains("express") || userPrompt.Contains("react") || userPrompt.Contains("node"))
            {
                return Task.FromResult("MERNProject");
            }
            else if (userPrompt.Contains("spring boot") || userPrompt.Contains("java spring") || userPrompt.Contains("java microservice"))
            {
                return Task.FromResult("SpringBootProject");
            }
            else if (userPrompt.Contains("php") || userPrompt.Contains("laravel") || userPrompt.Contains("symfony"))
            {
                return Task.FromResult("PhpProject");
            }
            else if (userPrompt.Contains(".net") || userPrompt.Contains("c#") || userPrompt.Contains("azure functions") || userPrompt.Contains("blazor") || userPrompt.Contains("maui"))
            {
                return Task.FromResult("DotNetProject");
            }
            else if (userPrompt.Contains("batch file") || userPrompt.Contains("script"))
            {
                return Task.FromResult("BatchFileGenerator");
            }
            else if (userPrompt.Contains("environment setup") || userPrompt.Contains("install") || userPrompt.Contains("configure"))
            {
                return Task.FromResult("EnvironmentSetup");
            }
            else if (userPrompt.Contains("documentation") || userPrompt.Contains("docs") || userPrompt.Contains("readme"))
            {
                return Task.FromResult("DocumentationGenerator");
            }
            // Default to GeneralChat if no specific mode is detected
            return Task.FromResult("GeneralChat");
        }
    }

    public interface INLPServices
    {
        Task<string> DetectProjectAndAIMode(string userPrompt);
    }
}
