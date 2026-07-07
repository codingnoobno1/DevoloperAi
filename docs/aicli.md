# AI CLI & Monorepo Setup Engine Architecture

This document outlines the architecture, prompts, and fallback mechanisms for the Syncro CLI and Desktop monorepo/setup configuration.

---

## 1. Project Layout Scan (`ls` / Directory Scan)

When a repository is cloned (for example, to `syncrorep/cloned_repos/pgconnect`), the configuration manager performs an immediate scan of the directory tree:

- Immediate subdirectories are listed (skipping platform directories like `.git`, `node_modules`, `venv`, `bin`, `obj`).
- Configuration signature files are probed (`package.json`, `requirements.txt`, `Cargo.toml`, `go.mod`, `.csproj`).

---

## 2. Port 3020 Gemini LLM Engine

Syncro hosts a local Express server on port `3020` configured with Google Gemini (usually `gemini-2.5-flash`). 

### Detection and Check
Before calling generative intelligence, the CLI executes an HTTP `GET` request to `http://localhost:3020/`.
- If the server responds with status `200 OK` (i.e., `"Gemini Express server running"`), the dynamic parser is engaged.
- If it fails (connection refused or timeout), the system defaults gracefully to **Hindsight Heuristics**.

### Prompt Pipeline
A `POST` is sent to `http://localhost:3020/gemini` with a structure description of the project directories and key config files.
The model returns a JSON array detailing target subfolders and their framework:
```json
[
  { "path": "frontend", "framework": "Next.js" },
  { "path": "backend", "framework": "Express/Node" }
]
```

---

## 3. Fallback Heuristics (LLM Offline / No-AI)

If the local AI service is unavailable, the engine falls back to a deterministic rule system:

1. **Subfolder Iteration**: Immediate folders are evaluated. If a subfolder contains a known configuration file (`package.json`, `go.mod`, etc.), it is marked as a project component.
2. **Framework Mapping**:
   - `package.json` $\rightarrow$ Next.js, Express, NestJS, Vite, or Generic Node.
   - `requirements.txt`/`pyproject.toml` $\rightarrow$ Django, FastAPI, Flask, or Generic Python.
   - `*.csproj` $\rightarrow$ ASP.NET Core or Blazor.
   - `go.mod` $\rightarrow$ Go.
   - `Cargo.toml` $\rightarrow$ Rust.
3. **Execution Sequencing**: Each mapped project component is queued for `AutoInstallEnvironment` (e.g., `npm install`) and `AutoRunDevServer` (e.g., `npm run dev`).

---

## 4. Isolated Execution Model

To guarantee CLI terminal or command prompt crashes do not crash the main Syncro Desktop software:

- **Isolated Dev Servers**: Spawned using `cmd.exe /K` with `UseShellExecute = true` in a separate console window. They run entirely independently in the OS.
- **Robust Command Resolution**: Invoking shell tools (`npm`, `pip`) on Windows routes via `cmd.exe /C` to avoid `Win32Exception` failures.
- **Graceful Error Catching**: Every process step is wrapped in `try/catch` and streamed to console logs.
