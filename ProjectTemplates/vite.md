---
id: vite
label: Vite React
language: TypeScript
kind: Frontend
version: 1.0.0
packageManager: npm
defaultPort: 5173
architectures: [flat]
defaultArchitecture: flat
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "Dev port",      default: 5173, type: int }
needs: [API_BASE_URL]
run: "npm run dev -- --port {{Port}}"
tags: [frontend, react, vite]
---

# Vite React Template (offline fallback)

A minimal Vite + React + TypeScript project used when the native `create-vite` CLI is unavailable.
Run `npm install` before starting.

## Shared

### file: README.md
```markdown
# {{ProjectName}}

Vite React app scaffolded by Syncro.

## Run
```
npm install
npm run dev
```
Open http://localhost:{{Port}}
```

### file: .gitignore
```text
node_modules/
dist/
.env*.local
.syncro_db/
```

## Architecture: flat

### file: package.json
```json
{
  "name": "{{ProjectName}}",
  "version": "1.0.0",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1"
  },
  "devDependencies": {
    "@vitejs/plugin-react": "^4.3.1",
    "typescript": "^5.5.4",
    "vite": "^5.4.0",
    "@types/react": "^18.3.3",
    "@types/react-dom": "^18.3.0"
  }
}
```

### file: vite.config.ts
```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: { port: {{Port}} },
});
```

### file: index.html
```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{{ProjectName}}</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

### file: src/main.tsx
```typescript
import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
```

### file: src/App.tsx
```typescript
export default function App() {
  return (
    <main style={{ padding: 40, fontFamily: "system-ui" }}>
      <h1>Welcome to {{ProjectName}}</h1>
      <p>Scaffolded by Syncro. Edit <code>src/App.tsx</code> to get started.</p>
    </main>
  );
}
```
