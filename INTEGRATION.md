# Syncro.Desktop ↔ S.A.N.K.A.L.P. Platform — Integration Map

## Overview

Syncro.Desktop is the **native Windows client** for the S.A.N.K.A.L.P. web platform. The two projects share the same backend (MongoDB + Next.js API), with the desktop app calling the `/api/mobile/...` route group which is purpose-built for non-browser clients (JWT auth instead of session cookies).

```
┌─────────────────────┐          JWT / Bearer         ┌──────────────────────────────┐
│  Syncro.Desktop     │  ◄──────────────────────────► │  S.A.N.K.A.L.P. Web Platform │
│  (.NET MAUI)        │    https://project-syncroo     │  (Next.js on Netlify)        │
│                     │         .netlify.app           │                              │
│  AuthService        │                                │  /api/mobile/login           │
│  PixelService       │                                │  /api/mobile/...             │
│  NetlifyService     │                                │  /api/admin/...              │
└─────────────────────┘                                └──────────────────────────────┘
```

---

## Authentication Flow

### Desktop → Web

| Desktop Service | Method | Web API Endpoint | Notes |
|---|---|---|---|
| `AuthService` | `Login()` | `POST /api/mobile/login` | Returns `{ token, user: { id, name, email } }` |
| `AuthService` | `Register()` | `POST /api/register` | Returns `{ userId, token, message }` |
| `AuthService` | `Logout()` | (local only) | Clears token + userId in memory |

After login, `AuthService.Token` is stored in memory. Every subsequent call from `PixelService` injects it as:
```
Authorization: Bearer <jwt>
```

---

## API Endpoint Mapping

### Endpoints the Desktop Currently Calls

| Desktop Service/Component | HTTP | Desktop calls | Web Platform route |
|---|---|---|---|
| `AuthService.Login` | POST | `/api/mobile/login` | `src/app/api/mobile/login/route.ts` |
| `AuthService.Register` | POST | `/api/register` | `src/app/api/register/route.ts` |
| `PixelService.GetProfile` | GET | `/api/mobile/user/profile` | `src/app/api/mobile/user/profile/route.ts` |
| `PixelService.GetUserProposals` | GET | `/api/mobile/proposals/user/:id` | `src/app/api/mobile/proposals/user/[id]/route.ts` |
| `PixelService.GetFeed` | GET | `/api/mobile/feed` | `src/app/api/mobile/feed/route.ts` |
| `PixelService.CreateProposal` | POST | `/api/proposals` | `src/app/api/proposals/route.ts` |
| `PixelService.DeleteProposal` | DELETE | `/api/proposals?id=:id` | `src/app/api/proposals/route.ts` |
| `PixelService.UpvoteProposal` | POST | `/api/votes` | `src/app/api/votes/route.ts` |
| `PixelService.AddComment` | POST | `/api/comments` | `src/app/api/comments/route.ts` |
| `PixelService.GetMarketplaceScripts` | GET | `/api/mobile/marketplace/scripts` | ⚠️ **NOT YET IMPLEMENTED** |
| `NetlifyService.GetSiteStatus` | GET | `api.netlify.com/api/v1/sites/:name` | External Netlify API |
| `NetlifyService.GetRecentDeploys` | GET | `api.netlify.com/api/v1/sites/:id/deploys` | External Netlify API |

---

## Full Mobile API Surface (Available but not yet used in Desktop)

The web platform has a rich `/api/mobile/...` layer. The desktop client only uses a subset. Here are all available endpoints that can be wired up:

### Social / Community
| Endpoint | Purpose |
|---|---|
| `GET /api/mobile/feed` | ✅ Already used — live proposal + activity feed |
| `GET /api/mobile/proposals/trending` | Trending proposals |
| `GET /api/mobile/proposals/:id` | Single proposal detail |
| `POST /api/mobile/proposals/vote` | Vote on a proposal (mobile-specific vote route) |
| `POST /api/mobile/proposals/join` | Join a proposal as contributor |
| `PATCH /api/mobile/proposals/update` | Update a proposal |
| `GET /api/mobile/activity` | Global activity stream |
| `GET /api/mobile/activity/trending` | Trending activity |
| `GET /api/mobile/activity/user/:id` | Activity for a specific user |

### Users & Profiles
| Endpoint | Purpose |
|---|---|
| `GET /api/mobile/user/profile` | ✅ Already used — own profile |
| `PATCH /api/mobile/user/profile/update` | Update profile (bio, skills, github, etc.) |
| `GET /api/mobile/user/:id` | Any user's public profile |
| `GET /api/mobile/user/:id/followers` | Follower list |
| `GET /api/mobile/user/:id/following` | Following list |
| `GET /api/mobile/user/:id/mutuals` | Mutual connections |
| `POST /api/mobile/user/connect` | Follow/unfollow a user |
| `GET /api/mobile/user/stats/:id` | Reputation, proposal count, follower stats |
| `PUT /api/mobile/user/:id/presence` | Update online presence status |
| `POST /api/mobile/user/skills/endorse` | Endorse a skill on another user's profile |

### Developers
| Endpoint | Purpose |
|---|---|
| `GET /api/mobile/developers/featured` | Featured developer profiles |
| `GET /api/mobile/developers/search` | Search developers by name/skill |
| `GET /api/mobile/builders/online` | Currently online builders |
| `GET /api/mobile/builders/rankings` | Reputation leaderboard |

### Projects & Progress
| Endpoint | Purpose |
|---|---|
| `GET /api/mobile/projects` | List all projects |
| `GET/POST /api/mobile/project-progress/tasks` | Project tasks |
| `GET /api/mobile/project-progress/tasks/project/:id` | Tasks for a specific project |
| `PATCH/DELETE /api/mobile/project-progress/tasks/:taskId` | Update/delete a task |
| `GET/POST /api/mobile/project-progress/weekly-reports` | Weekly reports |
| `GET /api/mobile/project-progress/activity` | Project activity log |
| `PATCH /api/mobile/project-progress/progress` | Update task progress |

### Git Integration
| Endpoint | Purpose |
|---|---|
| `POST /api/mobile/git/scan` | Scan a GitHub profile and store repo data |
| `POST /api/mobile/git/sync` | Sync latest commits/stats for a stored repo |
| `GET /api/mobile/git/user` | Get stored git repos for current user |

### Organizations
| Endpoint | Purpose |
|---|---|
| `GET /api/mobile/orgs` | List organizations |
| `POST /api/mobile/orgs/:id/join` | Join an org |

### Contributions
| Endpoint | Purpose |
|---|---|
| `GET/POST /api/mobile/contributions` | Log or list contributions |

### Comments
| Endpoint | Purpose |
|---|---|
| `GET/POST /api/mobile/comments` | Comments on proposals |
| `GET/PATCH/DELETE /api/mobile/comments/:commentId` | Single comment ops |
| `POST /api/mobile/comments/vote` | Vote on a comment |

### Votes
| Endpoint | Purpose |
|---|---|
| `GET /api/mobile/votes/my` | All votes cast by the current user |
| `POST /api/mobile/votes` | Cast a vote |

### Admin (from Desktop)
| Endpoint | Purpose |
|---|---|
| `GET/PATCH /api/mobile/admin/proposals` | Admin manage proposals |
| `GET/PATCH /api/mobile/admin/users` | Admin manage users |

---

## Shared Data Models

Both projects work on the same MongoDB documents. Here's the field alignment:

### User
| Web model field | Desktop `UserProfileModel` field |
|---|---|
| `_id` | `Id` |
| `name` | `Name` |
| `email` | `Email` |
| `avatar` | `Avatar` |
| `role` | `Role` |
| `universityName` | `UniversityName` |
| `skills` | `Skills` (List\<string\>) |

### Proposal
| Web model field | Desktop `ProposalModel` field |
|---|---|
| `_id` | `Id` |
| `title` | `Title` |
| `description` | `Description` |
| `status` | `Status` |
| `type` | `Type` |
| `stage` | `Stage` |
| `techStack` | `TechStack` (List\<string\>) |
| `totalVotes` | `TotalVotes` |
| `upvotes` | `Upvotes` |
| `commentsCount` | `CommentsCount` |
| `teamSize` | `TeamSize` |
| `createdAt` | `CreatedAt` |

---

## ⚠️ Gaps & Issues

### 1. Missing API Endpoint: Marketplace Scripts
**Desktop calls:** `GET /api/mobile/marketplace/scripts`  
**Web platform:** This route does not exist yet.  
**Current workaround:** `PixelService.GetMarketplaceScripts()` returns 3 hardcoded fallback items when the API is not found.  
**Fix needed:** Implement `src/app/api/mobile/marketplace/scripts/route.ts` on the web platform.

### 2. Auth: Register endpoint mismatch
**Desktop calls:** `POST /api/register` (not the mobile-prefixed version)  
**Web has both:** `/api/register` and `/api/mobile/register`  
**Recommendation:** Desktop should use `/api/mobile/register` for consistency, as it may have different response handling suited for token-based clients.

### 3. Proposal creation: not mobile-prefixed
**Desktop calls:** `POST /api/proposals` (no `/mobile/` prefix)  
**Web mobile route:** `POST /api/mobile/proposals` also exists  
**Recommendation:** Switch to `/api/mobile/proposals` to ensure JWT auth middleware is applied.

### 4. Desktop local project cache ≠ Platform projects
Projects created in the desktop (`ProjectService`) are stored **locally only** (`projects_cache.json`). They are not synced to the web platform's `Project` model.  
**Future work:** After a user creates a local project, optionally push metadata to `/api/mobile/projects` to register it in the platform.

### 5. Git repos not synced
The desktop runs `LibGit2SharpService` and `GitCommandService` locally, but the scanned repo data is not pushed to the platform's `GitRepo` model.  
**Available:** `POST /api/mobile/git/scan` and `POST /api/mobile/git/sync` exist on the web.  
**Future work:** After a git operation in the desktop, call the sync endpoint to update the user's GitHub metrics on their profile.

### 6. Profile editing not implemented in Desktop
`UserProfile.razor` only reads profile data. The web platform has `PATCH /api/mobile/user/profile/update` which is not yet called from the desktop.

---

## Connection Diagram (Full)

```
┌──────────────────────────────────────────────────────────────────┐
│                      MongoDB (shared)                             │
│  Users · Proposals · Projects · Tasks · Votes · Comments · ...   │
└───────────────────────────┬──────────────────────────────────────┘
                            │
              ┌─────────────▼──────────────┐
              │  Next.js API (Netlify)      │
              │  /api/mobile/...            │
              │  /api/admin/...             │
              │  /api/...  (web-only)       │
              └──────┬──────────┬───────────┘
                     │          │
         ┌───────────▼──┐  ┌────▼─────────────────────┐
         │  Web Browser  │  │   Syncro.Desktop          │
         │  (Next.js)    │  │   (.NET MAUI + Blazor)    │
         │  Session auth │  │   JWT Bearer auth         │
         │               │  │                           │
         │  /feed        │  │   /social  (Social.razor) │
         │  /ideas       │  │   /myproposals            │
         │  /profile     │  │   /profile                │
         │  /projects    │  │   + local dev tools:      │
         │  /admin       │  │     - AI Agent            │
         │               │  │     - Project scaffolding │
         │               │  │     - Git Manager         │
         │               │  │     - Syncro CLI          │
         │               │  │     - Env Manager         │
         │               │  │     - Marketplace         │
         └───────────────┘  └───────────────────────────┘
```
