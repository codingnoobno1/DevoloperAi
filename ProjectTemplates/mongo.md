---
id: mongo
label: MongoDB
language: NoSQL
kind: Database
version: 1.0.0
defaultPort: 27017
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "Mongo port",    default: 27017, type: int }
provides: [DATABASE_URL]
tags: [database, nosql, docker]
---

# MongoDB Template

A runnable MongoDB service via Docker Compose, plus a database initialization script.

## Shared

### file: docker-compose.yml
```yaml
services:
  mongo:
    image: mongo:6
    container_name: {{ProjectName}}_mongo
    restart: unless-stopped
    ports:
      - "{{Port}}:27017"
    environment:
      MONGO_INITDB_DATABASE: {{ProjectName}}
    volumes:
      - mongo_data:/data/db
      - ./init:/docker-entrypoint-initdb.d:ro

volumes:
  mongo_data:
```

### file: init/01-init.js
```javascript
// Runs once on first container start. Creates a collection so the DB is non-empty.
db = db.getSiblingDB('{{ProjectName}}');
db.createCollection('items');
db.items.insertOne({ name: 'sample', createdAt: new Date() });
```

### file: .env.example
```text
DATABASE_URL=mongodb://localhost:{{Port}}/{{ProjectName}}
```

### file: README.md
```markdown
# {{ProjectName}} — MongoDB

Start the database:
```
docker compose up -d
```
Connection string: `mongodb://localhost:{{Port}}/{{ProjectName}}`

Stop it:
```
docker compose down
```
```
