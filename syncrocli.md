# Syncro CLI - Documentation

Syncro CLI is a powerful, extensible command-line engine built into the Syncro Desktop agent. It provides automated project scaffolding, version control management, and environment diagnostics across multiple tech stacks.

## 🚀 Core Features

### 1. Multi-Stack Project Scaffolding (`init`)
The `init` command leverages specialized providers to bootstrap projects with industry-standard structures:
- **Flutter**: Full mobile/web app initialization.
- **MERN**: Automated setup for MongoDB, Express, React, and Node.js.
- **Python**: Virtual environment creation and core script scaffolding.
- **Dart**: Console application setup.
- **Java (Gradle)**: Enterprise-grade Java project structures using Gradle.
- **Go**: Module-based Go workspace initialization.

### 2. Intelligent Git Integration (`git`)
Direct access to Git operations optimized for the Syncro workflow:
- Automated staging and commit flows.
- Branch management.
- Remote synchronization.

### 3. System Diagnostics (`doctor`)
A diagnostic tool that checks for necessary dependencies (Flutter SDK, Node.js, Python, etc.) and ensures the local environment is ready for development.

### 4. AI-Enhanced Execution
The CLI is integrated with the **Syncro AI Engine**, allowing it to:
- Generate complex configurations during initialization.
- Provide contextual help for CLI commands.

---

## 🛠️ Architecture

Syncro CLI is built on a modular "Provider-Command" pattern:
- **`CliEngine`**: The core orchestrator that parses input and routes to registered commands.
- **`IProjectProvider`**: An abstraction layer allowing the CLI to support new tech stacks by simply adding a new provider.
- **`ProcessRunner`**: A robust execution layer that handles external process management and real-time log streaming.

---

## 📅 Roadmap (Future Features)

- [ ] **Custom Templates**: User-defined templates for `init` command.
- [ ] **Dependency Manager**: Unified command to update project dependencies across stacks.
- [ ] **Cloud Deploy**: Direct integration with Netlify and Vercel for instant deployment.
- [ ] **Hot Patching**: AI-driven bug fixing directly via CLI commands.

---

> [!NOTE]
> Syncro CLI is automatically added to your system PATH during the first launch of Syncro Desktop, allowing you to use `syncro` commands in any terminal.
