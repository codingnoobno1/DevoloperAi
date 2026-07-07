---
id: postgres
label: PostgreSQL
language: SQL
kind: Database
version: 1.0.0
defaultPort: 5432
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "Postgres port", default: 5432, type: int }
provides: [DATABASE_URL]
tags: [database, sql, docker]
---

# PostgreSQL Template

A runnable PostgreSQL service via Docker Compose, with a schema init script.

## Shared

### file: docker-compose.yml
```yaml
services:
  postgres:
    image: postgres:16
    container_name: {{ProjectName}}_postgres
    restart: unless-stopped
    ports:
      - "{{Port}}:5432"
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: {{ProjectName}}
    volumes:
      - pg_data:/var/lib/postgresql/data
      - ./init:/docker-entrypoint-initdb.d:ro

volumes:
  pg_data:
```

### file: init/01-schema.sql
```sql
CREATE TABLE IF NOT EXISTS items (
    id         SERIAL PRIMARY KEY,
    name       TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

INSERT INTO items (name) VALUES ('sample');
```

### file: .env.example
```text
DATABASE_URL=postgresql://postgres:postgres@localhost:{{Port}}/{{ProjectName}}
```

### file: README.md
```markdown
# {{ProjectName}} — PostgreSQL

Start the database:
```
docker compose up -d
```
Connection string: `postgresql://postgres:postgres@localhost:{{Port}}/{{ProjectName}}`

Stop it:
```
docker compose down
```
```
