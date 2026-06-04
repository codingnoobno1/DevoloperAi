# SYNCRO — Agent Vision Document

> **"One platform. Every stack. Every developer."**
> Syncro is an AI-native developer ecosystem that sells access to a community,
> a template marketplace, and an intelligent project engine — distributed across
> web, mobile, and desktop in a single unified experience.

---

## 1. What Syncro Is Selling

Syncro is **not just a tool.** It is a **developer membership platform.**

Members pay to get:

| What they get | How it's delivered |
|---|---|
| The **Syncro Desktop** app | Downloadable binary — gated behind paid tier |
| **Template Marketplace** | 100+ curated full-stack starter templates |
| **AI Hybrid Engine** | Parallel AI that generates schema, code, and configs simultaneously |
| **Community** (S.A.N.K.A.L.P. network) | Proposals, voting, project tracking, peer collaboration |
| **CLI Bridge** | Maps any frontend to any backend with a single command |
| **Priority support + template updates** | Ongoing value that keeps the subscription alive |

The free tier gets: community access (web only), public proposal feed, and read-only template browsing.  
**Paid tier unlocks:** Desktop download, template scaffold, AI engine, CLI Bridge, and project sync.

---

## 2. Platform Architecture — The Three Surfaces

Syncro ships as **three connected surfaces** that share one backend and one identity.

```
                     ┌──────────────────────────────────┐
                     │        SYNCRO BACKEND             │
                     │  MongoDB · Next.js API · JWT      │
                     │  Users · Projects · Templates     │
                     │  Proposals · AI Jobs · Payments   │
                     └────────┬──────────┬───────────────┘
                              │          │          │
              ┌───────────────▼──┐  ┌────▼───┐  ┌──▼────────────────┐
              │   WEB PLATFORM   │  │ MOBILE │  │  DESKTOP APP       │
              │  (Next.js)       │  │  APP   │  │  (Syncro.Desktop)  │
              │  Community hub   │  │Flutter │  │  AI + CLI + Git    │
              │  Template viewer │  │template│  │  Project engine    │
              │  Public profile  │  │ viewer │  │  Paid-tier only    │
              └──────────────────┘  └────────┘  └────────────────────┘
```

### Surface 1 — Web Platform (S.A.N.K.A.L.P.)
- Community feed, proposals, project tracking, public profiles
- Template **browsing** (preview + description — no download without paid tier)
- Landing page that markets the full Syncro ecosystem
- Acts as the **social proof engine**: shows real live projects built with Syncro templates

### Surface 2 — Mobile App (Flutter — via Template)
- Built using Syncro's own Flutter template as a **dogfooding showcase**
- Shows proposals, feed, notifications, user profile
- Members can browse templates, view their projects, and read weekly reports on the go
- **The app itself is a template** — users buy the Syncro Mobile Template to get this exact app pre-wired to their own backend

### Surface 3 — Desktop App (Syncro.Desktop — Paid-only Download)
- The core product: AI agent, CLI Bridge, Git manager, scaffold engine
- Gated: login checks membership tier before unlocking features
- **The download link itself is behind the paid member dashboard** on the web

---

## 3. The CLI Bridge — Frontend ↔ Backend Mapper

The CLI Bridge is Syncro's core technical differentiator. It is a **code-generation engine** that takes a frontend choice and a backend choice and produces a wired, runnable full-stack project in under 60 seconds.

### Command Syntax
```
syncro init <frontend> --backend <backend> --db <database> [options]
```

### Supported Matrix

| Frontend | Backend | Database | Output |
|---|---|---|---|
| `nextjs` | `express` | `mongodb` | Full MERN stack with API routes, auth, models |
| `nextjs` | `fastapi` | `postgresql` | Next.js + FastAPI + SQLAlchemy + CORS wired |
| `nextjs` | `dotnet` | `sqlserver` | Next.js + ASP.NET Core Web API + EF Core |
| `vite` | `express` | `mongodb` | Vite React SPA + Express REST API |
| `vite` | `flask` | `postgresql` | Vite React + Flask Blueprint API + psycopg2 |
| `flutter` | `express` | `mongodb` | Flutter mobile app + Express API + JWT auth |
| `flutter` | `fastapi` | `mongodb` | Flutter + FastAPI + Motor (async Mongo) |
| `flutter` | `dotnet` | `sqlserver` | Flutter + ASP.NET Core + EF Core |
| `flutter` | `flask` | `mysql` | Flutter + Flask + SQLAlchemy + MySQL |

### What the CLI generates for each combination

For every `syncro init` call, the CLI produces:

```
my-project/
├── frontend/               ← chosen frontend, scaffolded
│   ├── src/
│   ├── package.json / pubspec.yaml
│   └── .env.local          ← pre-wired with API URL
│
├── backend/                ← chosen backend, scaffolded
│   ├── src/ or app/
│   ├── routes/
│   ├── models/             ← AI-generated from --schema flag
│   ├── requirements.txt / package.json / .csproj
│   └── .env                ← pre-wired with DB connection string
│
├── docker-compose.yml      ← full local dev environment
├── .syncro/
│   ├── project.json        ← Syncro project metadata
│   └── schema.json         ← AI-generated data model
└── README.md               ← AI-generated docs
```

### AI-Enhanced Scaffold (`--ai` flag)
When `--ai` is passed, the Syncro AI engine reads the project name and description, infers likely domain models, and pre-generates:
- Database schema
- API route structure
- Basic CRUD for detected models
- `.env` templates with variable names
- README documentation

```
syncro init nextjs --backend fastapi --db postgresql --ai "e-commerce store with products, carts, and orders"
```

---

## 4. AI Parallel Hybrid — The Database Generator

The **AI Parallel Hybrid** is Syncro's AI-driven schema and migration engine. It runs multiple AI inference jobs **in parallel** — one for schema design, one for query optimization, one for migration scripts — and merges the results.

### How It Works

```
User input: "multi-tenant SaaS with users, orgs, subscriptions, and audit logs"
                              │
              ┌───────────────▼───────────────┐
              │     Syncro AI Parallel Engine  │
              │                               │
     ┌────────▼─────┐  ┌─────▼──────┐  ┌────▼──────────┐
     │  Schema Job  │  │  Query Job │  │  Migration Job │
     │  Infers all  │  │  Designs   │  │  Generates SQL │
     │  models and  │  │  indexes,  │  │  or Mongoose   │
     │  relations   │  │  relations │  │  migration     │
     └────────┬─────┘  └─────┬──────┘  └────┬──────────┘
              └──────────────▼───────────────┘
                     ┌────────────────┐
                     │  Merged Output │
                     │                │
                     │  schema.json   │
                     │  models/       │
                     │  migrations/   │
                     │  seed.sql      │
                     └────────────────┘
```

### Database Targets
| Database | Output format |
|---|---|
| MongoDB | Mongoose schemas + indexes |
| PostgreSQL | Prisma schema or raw SQL + migrations |
| MySQL | Sequelize models or raw DDL |
| SQLite | Drizzle schema or raw DDL |
| SQL Server | EF Core models + migrations |
| Firebase | Firestore collection structure + rules |

### Usage
```
# From within a Syncro project:
syncro db generate "users, posts, comments with threaded replies, likes"
syncro db generate --from-swagger ./api-spec.yaml
syncro db migrate --to postgresql --from mongodb
```

---

## 5. Template Marketplace — The Revenue Engine

The Marketplace is where Syncro templates are sold and distributed. It is the **primary recurring revenue stream** beyond subscriptions.

### Template Categories

| Category | Examples |
|---|---|
| **SaaS Starter** | Multi-tenant Next.js + Node API + Stripe billing |
| **Mobile + API** | Flutter app + FastAPI backend, pre-wired |
| **E-Commerce** | Next.js storefront + Express + Stripe + MongoDB |
| **Dashboard** | Admin panel (Next.js) + REST API + PostgreSQL |
| **Social Platform** | Community feed like S.A.N.K.A.L.P. itself — buyable template |
| **AI App** | Chat interface + vector DB + OpenAI/Groq integration |
| **DevTool** | CLI tool scaffold + npm publish setup |
| **Microservices** | Docker Compose multi-service starter |

### How Templates Work in Syncro

1. Member browses Marketplace (web or desktop)
2. Clicks **Use Template**
3. Syncro CLI pulls the template and hydrates it with the project name, database credentials, and any AI-generated customizations
4. The project lands in their local workspace, already runnable

```
syncro template use sankalp-community --name my-community --db mongodb
syncro template use flutter-api-starter --name my-app --backend fastapi
```

### Template Licensing
- **Free templates** — basic starters, available to all registered members
- **Pro templates** — sold individually ($9–$49) or included in paid subscription
- **Community templates** — submitted by paid members, revenue-shared (70/30 split)
- **Enterprise templates** — custom, sold direct

---

## 6. Business Model — Membership Tiers

```
┌────────────────────────────────────────────────────────────────┐
│  FREE TIER                                                      │
│  ● Web community access (S.A.N.K.A.L.P.)                      │
│  ● Public proposal feed, voting                                 │
│  ● Template marketplace browsing (no download)                  │
│  ● Public profile + followers                                   │
└────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────┐
│  PRO TIER  ($X/month or $Y/year)                               │
│  ● Everything in Free                                           │
│  ● ✅ Syncro.Desktop download                                   │
│  ● ✅ Full CLI Bridge (all frontend × backend combos)           │
│  ● ✅ AI Parallel Hybrid database generator                     │
│  ● ✅ Unlimited local project creation                          │
│  ● ✅ All free templates                                         │
│  ● ✅ 5 Pro template credits/month                              │
│  ● ✅ Git sync to platform profile                              │
│  ● ✅ Priority community features (pinned proposals, etc.)      │
└────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────┐
│  TEAM TIER  ($X/seat/month)                                    │
│  ● Everything in Pro                                            │
│  ● ✅ Shared org workspace on platform                          │
│  ● ✅ Team project tracking dashboard                           │
│  ● ✅ Admin panel access (S.A.N.K.A.L.P. admin for your org)   │
│  ● ✅ Unlimited Pro templates                                    │
│  ● ✅ Custom template publishing + revenue share                │
│  ● ✅ Dedicated Netlify deploy dashboard                        │
└────────────────────────────────────────────────────────────────┘
```

### Download Gate Implementation
In `AuthService` and `App.xaml.cs`:
1. On launch, Desktop calls `GET /api/mobile/user/profile`
2. Checks `user.subscriptionTier` field
3. If `tier === "free"` → show upgrade paywall screen, disable all features
4. If `tier === "pro"` or `"team"` → unlock full app

---

## 7. The Syncro Product Loop (How It Grows)

```
  Member joins free tier
        │
        ▼
  Browses community proposals on web
  Sees real projects built with Syncro templates
        │
        ▼
  Wants to build something similar
  Hits the "Use Template" gate → Upgrade prompt
        │
        ▼
  Upgrades to Pro → Downloads Syncro.Desktop
        │
        ▼
  Uses CLI Bridge + AI engine to scaffold project
  Pushes project to S.A.N.K.A.L.P. as a proposal
        │
        ▼
  Community sees it, votes, contributes
  Member gains reputation + followers
        │
        ▼
  Member publishes their template to Marketplace
  Earns revenue share → renews subscription to keep earning
        │
        ▼
  New members discover the template → join free → upgrade
        └──────── loop repeats ─────────────────────────────┘
```

---

## 8. Full Feature Checklist (Shipped vs Roadmap)

### ✅ Shipped (in current codebase)
- [x] Web community platform (S.A.N.K.A.L.P.) — proposals, voting, feed, profiles
- [x] Desktop app shell (MAUI + Blazor Hybrid)
- [x] JWT auth connecting Desktop to web backend
- [x] Social feed in Desktop (proposals + activity + Netlify events)
- [x] Local project creation + type detection
- [x] Syncro CLI (init, git, doctor commands)
- [x] Project providers: Flutter, MERN, Python, Dart, Go, Java Gradle
- [x] AI agent panel (Groq-powered, NLP mode detection)
- [x] Git manager (LibGit2Sharp + shell commands)
- [x] Environment manager (SDK detection dashboard)
- [x] Netlify deploy status in Desktop header
- [x] Marketplace UI (with fallback sample data)
- [x] My Proposals page in Desktop
- [x] Admin panel on web (users, proposals, projects, tasks, team)

### 🔨 In Progress / Near-term
- [ ] `/api/mobile/marketplace/scripts` endpoint on web backend
- [ ] Membership tier field on User model + subscription gate in Desktop
- [ ] Payment integration (Stripe) wired to tier unlock
- [ ] Local project → platform project sync
- [ ] Desktop Git operations → platform GitRepo sync

### 🗺️ Roadmap
- [ ] AI Parallel Hybrid database generator (`syncro db generate`)
- [ ] Full frontend × backend CLI Bridge matrix (all 9 combinations)
- [ ] Template Marketplace with purchase + download flow
- [ ] Mobile app Flutter template (dogfood the platform as a sellable template)
- [ ] Community template submission + revenue share backend
- [ ] `syncro db migrate` cross-database migration tool
- [ ] `syncro deploy` — push to Netlify/Vercel/Render from CLI
- [ ] Hot patch AI (`syncro fix <description>`) — AI-driven bug resolution
- [ ] Team workspace in Desktop (multi-user project view)
- [ ] Custom template authoring tool in Desktop

---

## 9. Positioning Statement

> **Syncro** is the only developer platform where the community, the tools, and the templates are one product.
> You don't buy a CLI. You don't buy a course. You buy membership to an ecosystem that scaffolds your projects,
> tracks your progress, connects you to collaborators, and sells your work — all from one place.

### Competitive positioning
| Tool | What it does | Syncro's edge |
|---|---|---|
| Vercel | Deploy web apps | Syncro deploys AND scaffolds AND manages community |
| GitHub Copilot | AI code suggestions | Syncro generates entire project structures end-to-end |
| create-t3-app | Opinionated Next.js scaffold | Syncro supports 9 frontend × backend combos with AI |
| Expo | Mobile app tooling | Syncro is Flutter-native and community-backed |
| Linear / Jira | Project management | Syncro is built for developers, not managers |
