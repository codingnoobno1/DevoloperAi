---
id: next
label: Next.js
language: TypeScript
kind: Frontend
version: 1.0.0
packageManager: npm
defaultPort: 3000
architectures: [flat]
defaultArchitecture: flat
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "Dev port",      default: 3000, type: int }
needs: [API_BASE_URL]
run: "npm run dev -- -p {{Port}}"
tags: [frontend, react, next]
---

# Next.js Template (offline fallback)

A minimal Next.js App-Router project used when the native `create-next-app` CLI is unavailable.
Run `npm install` before starting.

## Shared

### file: README.md
```markdown
# {{ProjectName}}

Next.js app scaffolded by Syncro.

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
.next/
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
  "scripts": {
    "dev": "next dev",
    "build": "next build",
    "start": "next start"
  },
  "dependencies": {
    "next": "^14.2.5",
    "react": "^18.3.1",
    "react-dom": "^18.3.1"
  },
  "devDependencies": {
    "typescript": "^5.5.4",
    "@types/node": "^20.14.0",
    "@types/react": "^18.3.3"
  }
}
```

### file: tsconfig.json
```json
{
  "compilerOptions": {
    "target": "ES2017",
    "lib": ["dom", "dom.iterable", "esnext"],
    "jsx": "preserve",
    "module": "esnext",
    "moduleResolution": "bundler",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "plugins": [{ "name": "next" }]
  },
  "include": ["next-env.d.ts", "**/*.ts", "**/*.tsx"],
  "exclude": ["node_modules"]
}
```

### file: next.config.js
```javascript
/** @type {import('next').NextConfig} */
const nextConfig = {};
module.exports = nextConfig;
```

### file: app/layout.tsx
```typescript
export const metadata = { title: "{{ProjectName}}" };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>{children}</body>
    </html>
  );
}
```

### file: app/page.tsx
```typescript
export default function Home() {
  return (
    <main style={{ padding: 40, fontFamily: "system-ui" }}>
      <h1>Welcome to {{ProjectName}}</h1>
      <p>Scaffolded by Syncro. Edit <code>app/page.tsx</code> to get started.</p>
    </main>
  );
}
```
