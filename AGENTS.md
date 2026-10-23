# Syncro.Desktop — Agent Context

## What this project is
A **Windows desktop developer tool** built with .NET 9 MAUI + Blazor Hybrid. It is the native desktop client for the S.A.N.K.A.L.P. community platform. It has two sides:
1. **Local dev tools** — AI agent, project scaffolding, Git manager, Syncro CLI, environment detection
2. **Platform integration** — social feed, proposals, profile pulled from `https://project-syncroo.netlify.app`

## Key files to know
| File | Role |
|---|---|
| `MauiProgram.cs` | DI registrations — all services registered here |
| `App.xaml.cs` | Window lifecycle, auth gate |
| `Components/Routes.razor` | Blazor router |
| `Components/Layout/MainLayout.razor` | Shell: header, sidebar, terminal button |
| `Components/Layout/NavMenu.razor` | Sidebar navigation |
| `Services/Auth/AuthService.cs` | JWT login/register against web API |
| `Services/PixelService.cs` | All S.A.N.K.A.L.P. API calls — proposals, feed, profile, votes, comments |
| `Services/NetlifyService.cs` | Netlify deploy status |
| `Services/ProjectService.cs` | Local project cache (JSON file in AppData) |
| `Services/SyncroCLI/SyncroCLIService.cs` | CLI orchestrator |
| `Services/SyncroCLI/Core/CliEngine.cs` | Command parser + router |
| `BusinessLogic/AIClient.cs` | Groq AI integration |
| `BusinessLogic/EnvironmentManager.cs` | Detects Node/Python/Java/.NET/Flutter installs |

## Web platform connection
- **Base URL:** `https://project-syncroo.netlify.app`
- **Auth:** JWT Bearer token stored in `AuthService.Token`
- **Routes used:** `/api/mobile/login`, `/api/mobile/feed`, `/api/mobile/user/profile`, `/api/mobile/proposals/user/:id`, `/api/proposals`, `/api/votes`, `/api/comments`
- **Full integration map:** see `INTEGRATION.md`
- **⚠️ Missing endpoint:** `GET /api/mobile/marketplace/scripts` — currently falls back to hardcoded data

## Paired project
The web platform lives at `D:\pixelclubaup\pixel-platform\`. Its mobile API source is in `src/app/api/mobile/`. When adding features that need both sides, check `INTEGRATION.md` first.

## Architecture pattern
- **Blazor pages** use `@inject` to get services
- **Services** are all DI singletons registered in `MauiProgram.cs`
- **MAUI navigation** (e.g., post-login) is done via `Application.Current.Windows[0].Page = new MainPage()`
- **Blazor navigation** (between pages) uses `NavigationManager` or `<NavLink>`

## Build & run
```
# Requires: .NET 9 SDK, Windows 10+
dotnet build
dotnet run
# Or open Syncro.Desktop.sln in Visual Studio 2022
```

## Component library
Uses **MudBlazor** for all UI components (`MudButton`, `MudTextField`, `MudGrid`, etc.) with a custom theme defined in `MainLayout.razor`'s `OnInitializedAsync`.
