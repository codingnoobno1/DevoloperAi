---
id: express
label: Express/Node
language: JavaScript
kind: Backend
version: 1.0.0
packageManager: npm
defaultPort: 5000
architectures: [flat, ntier, clean]
defaultArchitecture: flat
entry: server.js
entryByArchitecture:
  ntier: src/server.js
  clean: src/server.js
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "HTTP port",     default: 5000, type: int }
dependencies:
  - express@^4.19.0
  - cors@^2.8.5
  - dotenv@^16.4.5
provides: [API_BASE_URL]
needs: [DATABASE_URL]
postInstall: "npm install"
run: "node {entry}"
tags: [api, node, express]
---

# Express/Node Template

A reusable Express starter. **Flat** = one `server.js`. **N-Tier** = routes → controllers →
services → repositories → models. **Clean** = domain / application / infrastructure / interfaces.

## Shared

### file: .env.example
```text
PORT={{Port}}
DATABASE_URL=postgresql://postgres:postgres@localhost:5432/{{ProjectName}}
CORS_ORIGINS=http://localhost:3000
```

### file: .gitignore
```text
node_modules/
.env
npm-debug.log*
.syncro_db/
```

### file: README.md
```markdown
# {{ProjectName}}

Express service scaffolded by Syncro.

## Run
```
npm install
npm start
```
Server: http://localhost:{{Port}}
```

## Architecture: flat

### file: package.json
```json
{
  "name": "{{ProjectName}}",
  "version": "1.0.0",
  "main": "server.js",
  "scripts": { "start": "node server.js", "dev": "node server.js" },
  "dependencies": { "express": "^4.19.0", "cors": "^2.8.5", "dotenv": "^16.4.5" }
}
```

### file: server.js
```javascript
const express = require('express');
const cors = require('cors');
require('dotenv').config();

const app = express();
const PORT = process.env.PORT || {{Port}};

app.use(cors());
app.use(express.json());

app.get('/', (req, res) => res.json({ message: 'Welcome to {{ProjectName}}', status: 'online' }));
app.get('/api/health', (req, res) => res.json({ status: 'healthy' }));

app.listen(PORT, () => console.log(`Server running on port ${PORT}`));
```

## Architecture: ntier

### file: package.json
```json
{
  "name": "{{ProjectName}}",
  "version": "1.0.0",
  "main": "src/server.js",
  "scripts": { "start": "node src/server.js", "dev": "node src/server.js" },
  "dependencies": { "express": "^4.19.0", "cors": "^2.8.5", "dotenv": "^16.4.5" }
}
```

### file: src/config/index.js
```javascript
require('dotenv').config();
module.exports = {
  port: process.env.PORT || {{Port}},
  databaseUrl: process.env.DATABASE_URL || '',
};
```

### file: src/models/item.js
```javascript
class Item {
  constructor(id, name) { this.id = id; this.name = name; }
}
module.exports = Item;
```

### file: src/repositories/itemRepository.js
```javascript
const Item = require('../models/item');

class ItemRepository {
  constructor() { this.items = []; }
  list() { return this.items; }
  add(name) { const item = new Item(this.items.length + 1, name); this.items.push(item); return item; }
}
module.exports = new ItemRepository();
```

### file: src/services/itemService.js
```javascript
const repo = require('../repositories/itemRepository');

class ItemService {
  listItems() { return repo.list(); }
  createItem(name) { return repo.add(name); }
}
module.exports = new ItemService();
```

### file: src/controllers/itemController.js
```javascript
const service = require('../services/itemService');

exports.list = (req, res) => res.json(service.listItems());
exports.create = (req, res) => res.status(201).json(service.createItem(req.body.name));
exports.health = (req, res) => res.json({ status: 'healthy' });
```

### file: src/routes/index.js
```javascript
const express = require('express');
const ctrl = require('../controllers/itemController');

const router = express.Router();
router.get('/health', ctrl.health);
router.get('/items', ctrl.list);
router.post('/items', ctrl.create);
module.exports = router;
```

### file: src/app.js
```javascript
const express = require('express');
const cors = require('cors');
const routes = require('./routes');

const app = express();
app.use(cors());
app.use(express.json());
app.use('/api', routes);
app.get('/', (req, res) => res.json({ message: 'Welcome to {{ProjectName}}', status: 'online' }));
module.exports = app;
```

### file: src/server.js
```javascript
const app = require('./app');
const config = require('./config');
app.listen(config.port, () => console.log(`Server running on port ${config.port}`));
```

## Architecture: clean

### file: package.json
```json
{
  "name": "{{ProjectName}}",
  "version": "1.0.0",
  "main": "src/server.js",
  "scripts": { "start": "node src/server.js", "dev": "node src/server.js" },
  "dependencies": { "express": "^4.19.0", "cors": "^2.8.5", "dotenv": "^16.4.5" }
}
```

### file: src/domain/item.js
```javascript
// Enterprise entity — no framework imports
class Item {
  constructor(id, name) { this.id = id; this.name = name; }
}
module.exports = { Item };
```

### file: src/domain/itemRepositoryPort.js
```javascript
// Port (interface) the application depends on
class ItemRepositoryPort {
  list() { throw new Error('not implemented'); }
  add(item) { throw new Error('not implemented'); }
}
module.exports = { ItemRepositoryPort };
```

### file: src/application/itemUseCases.js
```javascript
const { Item } = require('../domain/item');

const listItems = (repo) => () => repo.list();
const createItem = (repo) => (name) => repo.add(new Item(null, name));

module.exports = { listItems, createItem };
```

### file: src/infrastructure/inMemoryItemRepository.js
```javascript
const { ItemRepositoryPort } = require('../domain/itemRepositoryPort');

class InMemoryItemRepository extends ItemRepositoryPort {
  constructor() { super(); this.items = []; }
  list() { return this.items; }
  add(item) { item.id = this.items.length + 1; this.items.push(item); return item; }
}
module.exports = { InMemoryItemRepository };
```

### file: src/interfaces/http/router.js
```javascript
const express = require('express');
const { InMemoryItemRepository } = require('../../infrastructure/inMemoryItemRepository');
const { listItems, createItem } = require('../../application/itemUseCases');

const repo = new InMemoryItemRepository();
const router = express.Router();

router.get('/health', (req, res) => res.json({ status: 'healthy' }));
router.get('/items', (req, res) => res.json(listItems(repo)()));
router.post('/items', (req, res) => res.status(201).json(createItem(repo)(req.body.name)));
module.exports = router;
```

### file: src/server.js
```javascript
const express = require('express');
const cors = require('cors');
require('dotenv').config();
const router = require('./interfaces/http/router');

const app = express();
app.use(cors());
app.use(express.json());
app.use('/api', router);

const PORT = process.env.PORT || {{Port}};
app.listen(PORT, () => console.log(`Server running on port ${PORT}`));
```
