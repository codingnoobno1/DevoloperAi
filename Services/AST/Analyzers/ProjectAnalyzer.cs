using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Syncro.Desktop.Services.AST.Core;
using Syncro.Desktop.Services.AST.Scanners;

namespace Syncro.Desktop.Services.AST.Analyzers;

public class ProjectAnalyzer
{
    private readonly FrameworkDetector _detector;
    private readonly ConfigScanner _configScanner;

    public ProjectAnalyzer(FrameworkDetector detector, ConfigScanner configScanner)
    {
        _detector = detector;
        _configScanner = configScanner;
    }

    public Task<AstContext> AnalyzeAsync(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Root directory not found: {rootPath}");
        }

        var configFiles = _configScanner.FindConfigFiles(rootPath);
        string framework = _detector.Detect(rootPath, configFiles);
        string language = _detector.DetectLanguage(framework);

        var ctx = new AstContext
        {
            RootPath = rootPath,
            ProjectLanguage = language.ToLower(),
            Framework = framework.ToLower()
        };

        return Task.FromResult(ctx);
    }
}
