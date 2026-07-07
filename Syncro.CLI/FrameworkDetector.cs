using Newtonsoft.Json.Linq;

namespace Syncro.CLI;

/// <summary>
/// Detects the language, framework, and ecosystem of a project directory
/// by checking file signals (config files, dependency manifests, source patterns).
/// </summary>
public static class FrameworkDetector
{
    public record DetectionResult(
        string Language,
        string Framework,
        string Ecosystem,
        string PackageManager,
        string DevCommand,
        string BuildCommand,
        int DefaultPort,
        bool RequiresVenv,
        bool RequiresNodeModules,
        string[] ExcludeDirs,
        string[] SourceRoots
    );

    public static DetectionResult Detect(string projectPath)
    {
        bool Has(string f) => File.Exists(Path.Combine(projectPath, f));
        bool HasDir(string d) => Directory.Exists(Path.Combine(projectPath, d));

        string? ReadText(string f)
        {
            string fp = Path.Combine(projectPath, f);
            return File.Exists(fp) ? File.ReadAllText(fp) : null;
        }

        bool PkgHas(string dep)
        {
            string? pkg = ReadText("package.json");
            return pkg != null && pkg.Contains($"\"{dep}\"");
        }

        bool CargoHas(string dep)
        {
            string? c = ReadText("Cargo.toml");
            return c != null && c.Contains(dep);
        }

        bool GoModHas(string dep)
        {
            string? m = ReadText("go.mod");
            return m != null && m.Contains(dep);
        }

        bool ReqHas(string dep)
        {
            string? r = ReadText("requirements.txt") ?? ReadText("pyproject.toml");
            return r != null && r.Contains(dep);
        }

        // ── Next.js ──────────────────────────────────────────────────────────
        if (Has("next.config.js") || Has("next.config.mjs") || Has("next.config.ts") || PkgHas("next"))
            return new("TypeScript", "Next.js", "Node.js", "npm",
                "npm run dev", "npm run build", 3000, false, true,
                [".next", "node_modules", ".vercel"], ["src/", "app/", "pages/", "components/"]);

        // ── NestJS ───────────────────────────────────────────────────────────
        if (Has("nest-cli.json") || PkgHas("@nestjs/core"))
            return new("TypeScript", "NestJS", "Node.js", "npm",
                "npm run start:dev", "npm run build", 3000, false, true,
                ["node_modules", "dist"], ["src/"]);

        // ── Vite ─────────────────────────────────────────────────────────────
        if (Has("vite.config.ts") || Has("vite.config.js"))
            return new("TypeScript", "Vite", "Node.js", "npm",
                "npm run dev", "npm run build", 5173, false, true,
                ["node_modules", "dist"], ["src/"]);

        // ── React Native ─────────────────────────────────────────────────────
        if (PkgHas("react-native"))
            return new("TypeScript", "React Native", "Node.js", "npm",
                "npx react-native start", "npx react-native build-android", 8081, false, true,
                ["node_modules", "android/build", "ios/build"], ["src/"]);

        // ── Express ──────────────────────────────────────────────────────────
        if (PkgHas("express") && !PkgHas("@nestjs/core"))
            return new("JavaScript", "Express", "Node.js", "npm",
                "npm run dev", "npm run build", 5000, false, true,
                ["node_modules", "dist"], ["src/"]);

        // ── Fastify ──────────────────────────────────────────────────────────
        if (PkgHas("fastify"))
            return new("TypeScript", "Fastify", "Node.js", "npm",
                "npm run dev", "npm run build", 3000, false, true,
                ["node_modules", "dist"], ["src/"]);

        // ── Hono ─────────────────────────────────────────────────────────────
        if (PkgHas("hono"))
            return new("TypeScript", "Hono", "Bun/Node.js", "npm",
                "npm run dev", "npm run build", 3000, false, true,
                ["node_modules", "dist"], ["src/"]);

        // ── Flutter ──────────────────────────────────────────────────────────
        if (Has("pubspec.yaml") && (ReadText("pubspec.yaml")?.Contains("flutter:") ?? false))
            return new("Dart", "Flutter", "Dart", "pub",
                "flutter run", "flutter build apk", 0, false, false,
                [".dart_tool", "build", ".pub-cache"], ["lib/"]);

        // ── Django ───────────────────────────────────────────────────────────
        if (Has("manage.py") && ReqHas("django"))
            return new("Python", "Django", "Python", "pip",
                "python manage.py runserver", "python manage.py collectstatic", 8000, true, false,
                ["__pycache__", ".venv", "venv", "staticfiles"], ["./"]);

        // ── FastAPI ──────────────────────────────────────────────────────────
        if (ReqHas("fastapi"))
            return new("Python", "FastAPI", "Python", "pip",
                "uvicorn main:app --reload", "uvicorn main:app", 8000, true, false,
                ["__pycache__", ".venv", "venv"], ["src/", "./"]);

        // ── Flask ────────────────────────────────────────────────────────────
        if (ReqHas("flask"))
            return new("Python", "Flask", "Python", "pip",
                "flask run", "gunicorn app:app", 5000, true, false,
                ["__pycache__", ".venv", "venv"], ["./"]);

        // ── ASP.NET Core ─────────────────────────────────────────────────────
        if (Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
        {
            string csproj = Directory.GetFiles(projectPath, "*.csproj")[0];
            string content = File.ReadAllText(csproj);
            if (content.Contains("UseMaui"))
                return new("C#", "MAUI", ".NET", "dotnet",
                    "dotnet run", "dotnet build", 0, false, false,
                    ["bin", "obj"], ["."]);
            if (content.Contains("Microsoft.AspNetCore.Components"))
                return new("C#", "Blazor", ".NET", "dotnet",
                    "dotnet run", "dotnet build", 5000, false, false,
                    ["bin", "obj"], ["."]);
            return new("C#", "ASP.NET Core", ".NET", "dotnet",
                "dotnet run", "dotnet build", 5000, false, false,
                ["bin", "obj"], ["."]);
        }

        // ── Spring Boot ──────────────────────────────────────────────────────
        if (Has("pom.xml") && (ReadText("pom.xml")?.Contains("spring-boot") ?? false))
            return new("Java", "Spring Boot", "Maven", "mvn",
                "./mvnw spring-boot:run", "./mvnw package", 8080, false, false,
                ["target"], ["src/main/java/"]);

        // ── Go Fiber / Echo / Gin ─────────────────────────────────────────────
        if (Has("go.mod"))
        {
            if (GoModHas("gofiber")) return new("Go", "Fiber", "Go", "go", "go run .", "go build", 3000, false, false, [], ["."]);
            if (GoModHas("labstack/echo")) return new("Go", "Echo", "Go", "go", "go run .", "go build", 1323, false, false, [], ["."]);
            if (GoModHas("gin-gonic")) return new("Go", "Gin", "Go", "go", "go run .", "go build", 8080, false, false, [], ["."]);
            return new("Go", "Go", "Go", "go", "go run .", "go build", 8080, false, false, [], ["."]);
        }

        // ── Rust ─────────────────────────────────────────────────────────────
        if (Has("Cargo.toml"))
        {
            if (CargoHas("axum")) return new("Rust", "Axum", "Cargo", "cargo", "cargo run", "cargo build --release", 3000, false, false, ["target"], ["src/"]);
            if (CargoHas("actix-web")) return new("Rust", "Actix", "Cargo", "cargo", "cargo run", "cargo build --release", 8080, false, false, ["target"], ["src/"]);
            return new("Rust", "Rust", "Cargo", "cargo", "cargo run", "cargo build --release", 8080, false, false, ["target"], ["src/"]);
        }

        // ── Laravel ──────────────────────────────────────────────────────────
        if (Has("artisan") && Has("composer.json"))
            return new("PHP", "Laravel", "Composer", "composer",
                "php artisan serve", "php artisan optimize", 8000, false, false,
                ["vendor", "storage/framework"], ["app/"]);

        // ── Ruby on Rails ────────────────────────────────────────────────────
        if (HasDir("config") && Has("config/application.rb"))
            return new("Ruby", "Ruby on Rails", "Bundler", "bundle",
                "rails server", "rails assets:precompile", 3000, false, false,
                ["tmp", "log", "vendor"], ["app/"]);

        // ── Kotlin / Android ─────────────────────────────────────────────────
        if (Has("build.gradle") || Has("build.gradle.kts"))
            return new("Kotlin", "Android", "Gradle", "gradle",
                "./gradlew installDebug", "./gradlew assembleRelease", 0, false, false,
                [".gradle", "build"], ["src/main/"]);

        // ── Tauri ────────────────────────────────────────────────────────────
        if (Has("tauri.conf.json"))
            return new("Rust", "Tauri", "Cargo + npm", "npm",
                "npm run tauri dev", "npm run tauri build", 0, false, true,
                ["target", "node_modules"], ["src/"]);

        // ── Electron ─────────────────────────────────────────────────────────
        if (PkgHas("electron"))
            return new("JavaScript", "Electron", "Node.js", "npm",
                "npm start", "npm run build", 0, false, true,
                ["node_modules", "dist"], ["src/"]);

        // ── Unknown Node.js project ──────────────────────────────────────────
        if (Has("package.json"))
            return new("JavaScript", "Node.js", "Node.js", "npm",
                "npm start", "npm run build", 3000, false, true,
                ["node_modules"], ["src/", "./"]);

        // ── Generic Python ───────────────────────────────────────────────────
        if (Has("requirements.txt") || Has("pyproject.toml") || Has("setup.py"))
            return new("Python", "Python", "Python", "pip",
                "python main.py", "python -m build", 0, true, false,
                ["__pycache__", ".venv", "venv"], ["./"]);

        // ── Fallback ─────────────────────────────────────────────────────────
        return new("Unknown", "Unknown", "Unknown", "unknown",
            "echo 'No run command detected'", "echo 'No build command detected'",
            0, false, false, [], ["."]);
    }
}
