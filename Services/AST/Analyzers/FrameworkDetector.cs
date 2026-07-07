using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Syncro.Desktop.Services.AST.Analyzers;

public class FrameworkDetector
{
    public string Detect(string rootPath, Dictionary<string, string> configFiles)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
        {
            return "Generic";
        }

        // Gather extensions to detect languages
        var allFiles = Directory.Exists(rootPath) 
            ? Directory.GetFiles(rootPath, "*.*", SearchOption.AllDirectories) 
            : Array.Empty<string>();

        // 1. Flutter / Dart
        if (configFiles.Keys.Any(k => k.Equals("pubspec.yaml", StringComparison.OrdinalIgnoreCase)))
        {
            return "Flutter";
        }

        // 2. Rust Web Frameworks (Actix-web, Rocket, Axum)
        if (configFiles.Keys.Any(k => k.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)))
        {
            string cargoPath = configFiles.FirstOrDefault(c => c.Key.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase)).Value;
            if (File.Exists(cargoPath))
            {
                string text = File.ReadAllText(cargoPath);
                if (text.Contains("actix-web")) return "Actix-web";
                if (text.Contains("rocket")) return "Rocket";
                if (text.Contains("axum")) return "Axum";
            }
            return "Rust Generic";
        }

        // 3. Go Web Frameworks (Gin, Echo, Fiber)
        if (configFiles.Keys.Any(k => k.Equals("go.mod", StringComparison.OrdinalIgnoreCase)))
        {
            string goModPath = configFiles.FirstOrDefault(c => c.Key.Equals("go.mod", StringComparison.OrdinalIgnoreCase)).Value;
            if (File.Exists(goModPath))
            {
                string text = File.ReadAllText(goModPath);
                if (text.Contains("github.com/gin-gonic/gin")) return "Gin (Go)";
                if (text.Contains("github.com/labstack/echo")) return "Echo (Go)";
                if (text.Contains("github.com/gofiber/fiber")) return "Fiber (Go)";
            }
            return "Go Generic";
        }

        // 4. PHP Frameworks (Laravel, Symfony)
        if (configFiles.Keys.Any(k => k.Equals("composer.json", StringComparison.OrdinalIgnoreCase)))
        {
            string compPath = configFiles.FirstOrDefault(c => c.Key.Equals("composer.json", StringComparison.OrdinalIgnoreCase)).Value;
            if (File.Exists(compPath))
            {
                string text = File.ReadAllText(compPath);
                if (text.Contains("laravel/framework")) return "Laravel";
                if (text.Contains("symfony/framework-bundle")) return "Symfony";
            }
            return "PHP Generic";
        }

        // 5. Ruby on Rails
        if (configFiles.Keys.Any(k => k.Equals("Gemfile", StringComparison.OrdinalIgnoreCase)))
        {
            return "Ruby on Rails";
        }

        // 6. Node.js Ecosystem (Next.js, NestJS, Express, Angular, Vite, React Native, SvelteKit, Nuxt.js, Electron)
        if (configFiles.Keys.Any(k => k.Equals("package.json", StringComparison.OrdinalIgnoreCase)))
        {
            string pkgPath = configFiles.FirstOrDefault(c => c.Key.Equals("package.json", StringComparison.OrdinalIgnoreCase)).Value;
            if (File.Exists(pkgPath))
            {
                string text = File.ReadAllText(pkgPath);
                if (text.Contains("\"next\"")) return "Next.js";
                if (text.Contains("\"@nestjs/core\"")) return "NestJS";
                if (text.Contains("\"express\"")) return "Express/Node";
                if (text.Contains("\"@angular/core\"")) return "Angular";
                if (text.Contains("\"react-native\"")) return "React Native";
                if (text.Contains("\"@sveltejs/kit\"")) return "SvelteKit";
                if (text.Contains("\"nuxt\"")) return "Nuxt.js";
                if (text.Contains("\"electron\"")) return "Electron";
                if (text.Contains("\"vite\"")) return "Vite Project";
            }
            return "Node.js Generic";
        }

        // 7. Python Frameworks (FastAPI, Flask, Django, PyTorch, TensorFlow)
        if (configFiles.Keys.Any(k => k.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase) || 
                                      k.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase) ||
                                      k.Equals("Pipfile", StringComparison.OrdinalIgnoreCase)))
        {
            string reqPath = configFiles.FirstOrDefault(c => c.Key.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrEmpty(reqPath))
            {
                reqPath = configFiles.FirstOrDefault(c => c.Key.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase)).Value;
            }

            if (!string.IsNullOrEmpty(reqPath) && File.Exists(reqPath))
            {
                string text = File.ReadAllText(reqPath);
                if (text.Contains("fastapi", StringComparison.OrdinalIgnoreCase)) return "FastAPI";
                if (text.Contains("flask", StringComparison.OrdinalIgnoreCase)) return "Flask";
                if (text.Contains("django", StringComparison.OrdinalIgnoreCase)) return "Django";
                if (text.Contains("torch", StringComparison.OrdinalIgnoreCase)) return "PyTorch Machine Learning";
                if (text.Contains("tensorflow", StringComparison.OrdinalIgnoreCase)) return "TensorFlow Machine Learning";
            }
            return "Python Generic";
        }

        // 8. C# Frameworks (.NET MAUI, WPF, Windows Forms, ASP.NET Core)
        var csprojFile = allFiles.FirstOrDefault(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(csprojFile) && File.Exists(csprojFile))
        {
            string text = File.ReadAllText(csprojFile);
            if (text.Contains("<UseMaui>true</UseMaui>", StringComparison.OrdinalIgnoreCase)) return ".NET MAUI";
            if (text.Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase)) return "WPF Windows App";
            if (text.Contains("<UseWindowsForms>true</UseWindowsForms>", StringComparison.OrdinalIgnoreCase)) return "Windows Forms App";
            if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)) return "ASP.NET Core";
            return ".NET Console / Library";
        }

        // 9. Java / Kotlin Ecosystem (Spring Boot, Android Native)
        if (configFiles.Keys.Any(k => k.Equals("pom.xml", StringComparison.OrdinalIgnoreCase) || 
                                      k.Equals("build.gradle", StringComparison.OrdinalIgnoreCase) ||
                                      k.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase)))
        {
            string buildPath = configFiles.FirstOrDefault(c => c.Key.Equals("pom.xml", StringComparison.OrdinalIgnoreCase)).Value;
            if (string.IsNullOrEmpty(buildPath))
            {
                buildPath = configFiles.FirstOrDefault(c => c.Key.Equals("build.gradle", StringComparison.OrdinalIgnoreCase)).Value;
            }
            if (string.IsNullOrEmpty(buildPath))
            {
                buildPath = configFiles.FirstOrDefault(c => c.Key.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase)).Value;
            }

            if (!string.IsNullOrEmpty(buildPath) && File.Exists(buildPath))
            {
                string text = File.ReadAllText(buildPath);
                if (text.Contains("spring-boot") || text.Contains("springframework.boot")) return "Spring Boot";
                if (text.Contains("com.android.application") || text.Contains("android.tools.build")) return "Android Native";
            }
            return "Java/Kotlin Generic";
        }

        // 10. Swift / iOS Native
        if (allFiles.Any(f => f.EndsWith(".swift", StringComparison.OrdinalIgnoreCase) || f.Contains(".xcodeproj")))
        {
            return "iOS Native SwiftUI/UIKit";
        }

        // 11. C++ / C CMake Project
        if (configFiles.Keys.Any(k => k.Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)))
        {
            return "CMake C/C++ Project";
        }

        // 12. Fallbacks based on prevalent file extensions in directory
        var extensions = allFiles
            .Where(f => Path.HasExtension(f))
            .Select(f => Path.GetExtension(f).ToLower())
            .GroupBy(ext => ext)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .Take(3)
            .ToList();

        if (extensions.Contains(".py")) return "Python Generic";
        if (extensions.Contains(".cs")) return ".NET Console / Library";
        if (extensions.Contains(".js") || extensions.Contains(".ts") || extensions.Contains(".tsx")) return "Node.js Generic";
        if (extensions.Contains(".cpp") || extensions.Contains(".cc") || extensions.Contains(".h") || extensions.Contains(".hpp")) return "C/C++ Generic";
        if (extensions.Contains(".java")) return "Java/Kotlin Generic";
        if (extensions.Contains(".dart")) return "Flutter";
        if (extensions.Contains(".php")) return "PHP Generic";
        if (extensions.Contains(".rb")) return "Ruby Generic";
        if (extensions.Contains(".go")) return "Go Generic";
        if (extensions.Contains(".rs")) return "Rust Generic";
        if (extensions.Contains(".swift")) return "iOS Native SwiftUI/UIKit";

        return "Generic";
    }

    public string DetectLanguage(string framework)
    {
        return framework switch
        {
            "Next.js" => "TypeScript/JavaScript",
            "NestJS" => "TypeScript",
            "Express/Node" => "JavaScript",
            "Angular" => "TypeScript",
            "React Native" => "JavaScript/TypeScript",
            "SvelteKit" => "JavaScript/TypeScript",
            "Nuxt.js" => "TypeScript/JavaScript/Vue",
            "Electron" => "JavaScript/TypeScript",
            "Vite Project" => "JavaScript/TypeScript",
            "Node.js Generic" => "JavaScript/TypeScript",

            "FastAPI" => "Python",
            "Flask" => "Python",
            "Django" => "Python",
            "PyTorch Machine Learning" => "Python",
            "TensorFlow Machine Learning" => "Python",
            "Python Generic" => "Python",

            "ASP.NET Core" => "C#",
            ".NET MAUI" => "C#",
            "WPF Windows App" => "C#",
            "Windows Forms App" => "C#",
            ".NET Console / Library" => "C#",

            "Spring Boot" => "Java/Kotlin",
            "Android Native" => "Kotlin/Java",
            "Java/Kotlin Generic" => "Java/Kotlin",

            "Flutter" => "Dart",

            "Laravel" => "PHP",
            "Symfony" => "PHP",
            "PHP Generic" => "PHP",

            "Ruby on Rails" => "Ruby",
            "Ruby Generic" => "Ruby",

            "Gin (Go)" => "Go",
            "Echo (Go)" => "Go",
            "Fiber (Go)" => "Go",
            "Go Generic" => "Go",

            "Actix-web" => "Rust",
            "Rocket" => "Rust",
            "Axum" => "Rust",
            "Rust Generic" => "Rust",

            "iOS Native SwiftUI/UIKit" => "Swift/Objective-C",
            "CMake C/C++ Project" => "C++/C",
            "C/C++ Generic" => "C++/C",

            _ => "Unknown"
        };
    }
}
