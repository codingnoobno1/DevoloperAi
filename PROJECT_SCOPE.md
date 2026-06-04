# Syncro.Desktop — Project Scope & Workflow

## 1. What Is This?

**Syncro.Desktop** is a Windows desktop application built with **.NET 9 MAUI + Blazor Hybrid**. It is the native desktop client for the **S.A.N.K.A.L.P. / Pixel platform** — giving club members a local developer workstation with AI assistance, project scaffolding, Git management, and a live social feed that syncs with the web platform.

**Tech stack:** .NET 9 MAUI · Blazor Hybrid · MudBlazor · C# · LibGit2Sharp · Groq AI (via AIClient)

**Backend:** `https://project-syncroo.netlify.app` — the deployed Next.js web platform

---

## 2. Application Architecture

```
┌────────────────────────────────────────────────────────────┐
│                     .NET MAUI Host                          │
│  App.xaml.cs   LoginPage.xaml   MainPage.xaml              │
│                TerminalPage.xaml  MobileMarketplacePage.xaml│
└────────────────────────┬───────────────────────────────────┘
                         │  BlazorWebView (bridge)
┌────────────────────────▼───────────────────────────────────┐
│                    Blazor UI Layer                          │
│  Components/Routes.razor → MainLayout.razor                │
│  Pages:  Home · MyProjects · GitManager · Social           │
│          MyProposals · UserProfile · Marketplace           │
│          MeetingsAndTarget · Auth/Login                     │
│  Shared: SyncroTerminal · ProjectCard · CreateProjectDialog │
└────────────────────────┬───────────────────────────────────┘
                         │  @inject (DI)
┌────────────────────────▼───────────────────────────────────┐
│                   Services Layer (DI Singletons)            │
│                                                             │
│  Auth         AuthService          JWT auth to API         │
│  Platform     PixelService         S.A.N.K.A.L.P. feed/proposals│
│  Platform     NetlifyService       Deploy status           │
│  Local        ProjectService       Local project cache     │
│  Local        FlutterService       Flutter SDK integration  │
│  Local        GitCommandService    Git shell commands      │
│  Local        LibGit2SharpService  Programmatic Git ops    │
│  AI           AIClient             Groq AI engine          │
│  CLI          SyncroCLIService     CLI orchestrator        │
│  CLI          CliEngine            Command parser + router  │
│  Generators   MERNProjectGeneratorService                  │
│  Generators   SpringBootProjectGeneratorService            │
│  Generators   PhpProjectGeneratorService                   │
│  Generators   DotNetProjectGeneratorService                │
│  Env          EnvironmentManager   Detect installed SDKs   │
│  Util         DocumentationGeneratorService                │
│  Util         BatchFileExecutionService                    │
│  Util         CommandExecutionService                      │
│  Util         QuickStartService                            │
└────────────────────────────────────────────────────────────┘
```

---

## 3. Navigation & Pages

### Main Navigation (NavMenu.razor)
```
Sidebar
│
├── [New Project]  ── opens CreateProjectDialog
│
├── Dashboard        /          → Home.razor
├── My Projects      /myprojects → MyProjects.razor
├── Git Manager      /gitmanager → GitManager.razor
├── Targets          /meetingsandtarget → MeetingsAndTarget.razor
│
│  ── SANKALP section ──
│
├── My Profile       /profile    → UserProfile.razor
├── My Proposals     /myproposals → MyProposals.razor
├── Social Feed      /social     → Social.razor
├── Marketplace      /marketplace → Marketplace.razor
└── Mobile Store     (new MAUI Window) → MobileMarketplacePage
```

### Separate MAUI Windows (spawned on demand)
| Window | Purpose |
|---|---|
| `TerminalPage` | Full Syncro CLI terminal emulator |
| `MobileMarketplacePage` | Browse/install community scripts |

---

## 4. Core Feature Areas

### 4.1 Authentication (`/login`)
- **Login tab** — email + password → `POST /api/mobile/login` → JWT stored in `AuthService`
- **Register tab** — full registration form → `POST /api/register`
- Dev bypass: `email=dev / password=password` skips API for local testing
- On success → navigates MAUI main window to `MainPage` (loads Blazor app)

### 4.2 Local Project Management (`/myprojects`)
Projects are stored locally in `%LocalAppData%/SyncroDesktop/projects_cache.json`.

| Action | How |
|---|---|
| **Create** | Opens `CreateProjectDialog` → picks name, path, type → `SyncroCLIService.CreateProject(stack, name, dir)` |
| **Import** | Folder picker → auto-detects type via `DetectProjectType()` |
| **Delete** | Removes from local cache only |

**Project type detection priority:**
1. `pubspec.yaml` → Mobile App (Flutter)
2. `package.json` → Next.js / Vite / React / Node.js (reads content)
3. `requirements.txt` / `*.py` → Python
4. `go.mod` → Go
5. `tsconfig.json` → TypeScript
6. Fallback → Generic Project

### 4.3 AI Agent Panel (Dashboard)
The AI agent is powered by Groq and handles:
- **General Chat** — coding Q&A, general help
- **Batch File Generator** — creates `.bat` automation scripts
- **Environment Setup** — generates + executes SDK install scripts
- **Project Generators** — bootstraps full stack projects via NLP intent detection
- **Documentation Generator** — auto-generates docs from project context

**Mode selection:** `INLPServices.DetectMode(prompt)` classifies natural language input into the correct handler.

### 4.4 Syncro CLI (Terminal Window)

Built on a `CliEngine` with a **Provider-Command** pattern:

```
CliEngine
├── CommandParser      — tokenizes input
├── CommandContext     — carries working directory, output stream
│
├── Commands
│   ├── InitCommand    — delegates to IProjectProvider
│   ├── GitCommand     — delegates to GitProvider sub-services
│   ├── DoctorCommand  — scans environment health
│   └── ScriptCommand  — executes .bat/.sh scripts
│
├── Providers
│   ├── FlutterProvider
│   ├── MernProvider
│   ├── PythonProvider
│   ├── DartProvider
│   ├── GoProvider
│   └── JavaGradleProvider
│
└── Execution
    ├── ProcessRunner  — spawns external processes, streams output
    ├── ScriptRunner   — runs generated scripts
    └── ElevationService — UAC elevation for admin operations
```

**Usage examples:**
```
syncro init flutter my_app
syncro init mern ecommerce-api
syncro git commit "feat: add user auth"
syncro doctor
```

### 4.5 Git Manager (`/gitmanager`)
Full Git workflow through two layers:
- **`GitCommandService`** — shell-level git commands
- **`LibGit2SharpService`** — programmatic Git via LibGit2Sharp

Sub-services in `SyncroCLI/Providers/Git/`:
- `GitProvider` — main coordinator
- `GitRepoService` — repo init, clone, status
- `GitChangeService` — stage, commit, diff
- `GitBranchService` — branch create, checkout, merge
- `GitErrorHandler` — normalizes Git error messages

### 4.6 S.A.N.K.A.L.P. Social Integration

**Social Feed** (`/social`) — pulls from the web platform:
- Live feed of proposals + activity events from `GET /api/mobile/feed`
- Netlify deploy events interleaved from `NetlifyService`
- Upvote a proposal: `POST /api/votes`
- Comment on a proposal: `POST /api/comments`
- System metrics sidebar (active proposals count, activity count)

**My Proposals** (`/myproposals`):
- Lists all proposals by the logged-in user (`GET /api/mobile/proposals/user/:id`)
- Create proposal dialog (title, description, type): `POST /api/proposals`
- Delete proposal: `DELETE /api/proposals?id=:id`
- Status indicators with colour coding (active/pending/proposal/rejected)

**My Profile** (`/profile`):
- Fetches user profile from `GET /api/mobile/user/profile`
- Shows name, email, university, skills, avatar

### 4.7 Marketplace (`/marketplace`)
Browses community-shared automation scripts from `GET /api/mobile/marketplace/scripts`.
Falls back to 3 hardcoded sample scripts if the API endpoint is not ready.

### 4.8 Environment Manager
`EnvironmentManager` auto-detects installed tools on the local machine:
- Node.js, Python, Java, .NET SDK, Flutter
- Displays name, version, path, availability in the `EnvironmentPanel` dashboard widget

### 4.9 Netlify Integration
`NetlifyService` connects to `https://api.netlify.com/api/v1`:
- `GetSiteStatus(siteName)` — checks status of the `project-syncroo` deployment
- `GetRecentDeploys(siteId)` — fetches last N deploys
- Status shown live in the header bar ("Netlify: READY / BUILDING / ERROR")

---

## 5. Service Dependency Graph (DI)

```
MauiProgram.cs registers (all Singleton):
│
├── HttpClient (built-in factory)
├── AuthService          (needs HttpClient)
├── PixelService         (needs HttpClient, AuthService)
├── NetlifyService       (needs HttpClient)
├── AIClient             (needs .env API keys)
├── EnvironmentManager
├── SyncroCLIService     (orchestrates CLI)
├── FlutterService
├── ProjectService       (needs FlutterService, SyncroCLIService)
├── FolderCreationService
├── BatchFileExecutionService
├── CommandExecutionService
├── MERNProjectGeneratorService
├── SpringBootProjectGeneratorService
├── PhpProjectGeneratorService
├── DotNetProjectGeneratorService
├── INLPServices         → SimpleNLPServices (concrete)
├── DocumentationGeneratorService
├── GitCommandService
├── QuickStartService
└── Git.FetchProject
```

---

## 6. Environment Configuration (`.env`)

Loaded at startup via `DotEnv.Load()`. Expected keys include AI API credentials (Groq key) used by `AIClient`.

---

## 7. Platform Targets

The project targets Windows 10 (`net9.0-windows10.0.19041.0`). Android, iOS, MacCatalyst, and Tizen platform stubs exist in `Platforms/` but are not the primary target.
