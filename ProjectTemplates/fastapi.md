---
id: fastapi
label: FastAPI
language: Python
kind: Backend
version: 1.0.0
packageManager: pip
defaultPort: 8000
architectures: [flat, ntier, clean]
defaultArchitecture: flat
entry: main.py
entryByArchitecture:
  ntier: main.py
  clean: app/api/main.py
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "MyApp" }
  - { key: Port,        prompt: "HTTP port",     default: 8000, type: int }
  - { key: PyPackage,   prompt: "Python package root", default: "app" }
dependencies:
  - fastapi>=0.110.0
  - uvicorn[standard]>=0.28.0
  - pydantic>=2.6.0
  - pydantic-settings>=2.2.0
provides: [API_BASE_URL]
needs: [DATABASE_URL]
postInstall: "pip install -r requirements.txt"
run: "uvicorn {entry} --reload --port {{Port}}"
tags: [api, python, async]
---

# FastAPI Template

A standard, reusable FastAPI starter. Summon it Flat (prototype), N-Tier (layered), or Clean
(domain-centric). `{{ProjectName}}`, `{{Port}}`, `{{PyPackage}}` are substituted at summon time.

## Shared

### file: README.md
```markdown
# {{ProjectName}}

FastAPI service scaffolded by Syncro.

## Run
```
pip install -r requirements.txt
uvicorn main:app --reload --port {{Port}}
```
Open http://localhost:{{Port}}/docs
```

### file: .env.example
```text
APP_NAME={{ProjectName}}
PORT={{Port}}
DATABASE_URL=postgresql://postgres:postgres@localhost:5432/{{ProjectName}}
CORS_ORIGINS=http://localhost:3000
```

### file: .gitignore
```text
__pycache__/
*.pyc
.venv/
venv/
.env
.syncro_db/
```

### file: Dockerfile
```dockerfile
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
EXPOSE {{Port}}
CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "{{Port}}"]
```

## Architecture: flat

### file: requirements.txt
```text
fastapi>=0.110.0
uvicorn[standard]>=0.28.0
pydantic>=2.6.0
```

### file: main.py
```python
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

app = FastAPI(title="{{ProjectName}}", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"], allow_credentials=True,
    allow_methods=["*"], allow_headers=["*"],
)

@app.get("/")
def read_root():
    return {"message": "Welcome to {{ProjectName}}", "status": "online"}

@app.get("/api/health")
def health():
    return {"status": "healthy"}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port={{Port}}, reload=True)
```

## Architecture: ntier

### file: requirements.txt
```text
fastapi>=0.110.0
uvicorn[standard]>=0.28.0
pydantic>=2.6.0
pydantic-settings>=2.2.0
```

### file: {{PyPackage}}/__init__.py
```python
```

### file: {{PyPackage}}/core/config.py
```python
from pydantic_settings import BaseSettings

class Settings(BaseSettings):
    app_name: str = "{{ProjectName}}"
    port: int = {{Port}}
    database_url: str = "sqlite:///./app.db"
    class Config:
        env_file = ".env"

settings = Settings()
```

### file: {{PyPackage}}/models/item.py
```python
from pydantic import BaseModel

class Item(BaseModel):
    id: int
    name: str
    description: str | None = None
```

### file: {{PyPackage}}/repositories/item_repository.py
```python
from {{PyPackage}}.models.item import Item

class ItemRepository:
    """In-memory repository. Swap for a DB-backed one in infrastructure."""
    def __init__(self) -> None:
        self._items: list[Item] = []

    def list(self) -> list[Item]:
        return self._items

    def add(self, item: Item) -> Item:
        self._items.append(item)
        return item
```

### file: {{PyPackage}}/services/item_service.py
```python
from {{PyPackage}}.models.item import Item
from {{PyPackage}}.repositories.item_repository import ItemRepository

class ItemService:
    def __init__(self, repo: ItemRepository) -> None:
        self._repo = repo

    def list_items(self) -> list[Item]:
        return self._repo.list()

    def create_item(self, item: Item) -> Item:
        return self._repo.add(item)
```

### file: {{PyPackage}}/api/routes.py
```python
from fastapi import APIRouter
from {{PyPackage}}.models.item import Item
from {{PyPackage}}.services.item_service import ItemService
from {{PyPackage}}.repositories.item_repository import ItemRepository

router = APIRouter(prefix="/api")
_service = ItemService(ItemRepository())

@router.get("/health")
def health():
    return {"status": "healthy"}

@router.get("/items", response_model=list[Item])
def list_items():
    return _service.list_items()

@router.post("/items", response_model=Item)
def create_item(item: Item):
    return _service.create_item(item)
```

### file: main.py
```python
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from {{PyPackage}}.core.config import settings
from {{PyPackage}}.api.routes import router

app = FastAPI(title=settings.app_name, version="1.0.0")
app.add_middleware(CORSMiddleware, allow_origins=["*"],
                   allow_credentials=True, allow_methods=["*"], allow_headers=["*"])
app.include_router(router)

@app.get("/")
def root():
    return {"message": f"Welcome to {settings.app_name}", "status": "online"}
```

## Architecture: clean

### file: requirements.txt
```text
fastapi>=0.110.0
uvicorn[standard]>=0.28.0
pydantic>=2.6.0
pydantic-settings>=2.2.0
```

### file: {{PyPackage}}/__init__.py
```python
```

### file: {{PyPackage}}/domain/entities.py
```python
from dataclasses import dataclass

@dataclass
class Item:
    id: int
    name: str
    description: str | None = None
```

### file: {{PyPackage}}/domain/ports.py
```python
from abc import ABC, abstractmethod
from {{PyPackage}}.domain.entities import Item

class ItemRepositoryPort(ABC):
    @abstractmethod
    def list(self) -> list[Item]: ...
    @abstractmethod
    def add(self, item: Item) -> Item: ...
```

### file: {{PyPackage}}/application/use_cases.py
```python
from {{PyPackage}}.domain.entities import Item
from {{PyPackage}}.domain.ports import ItemRepositoryPort

class ListItems:
    def __init__(self, repo: ItemRepositoryPort) -> None:
        self._repo = repo
    def __call__(self) -> list[Item]:
        return self._repo.list()

class CreateItem:
    def __init__(self, repo: ItemRepositoryPort) -> None:
        self._repo = repo
    def __call__(self, item: Item) -> Item:
        return self._repo.add(item)
```

### file: {{PyPackage}}/infrastructure/memory_repository.py
```python
from {{PyPackage}}.domain.entities import Item
from {{PyPackage}}.domain.ports import ItemRepositoryPort

class InMemoryItemRepository(ItemRepositoryPort):
    def __init__(self) -> None:
        self._items: list[Item] = []
    def list(self) -> list[Item]:
        return self._items
    def add(self, item: Item) -> Item:
        self._items.append(item); return item
```

### file: {{PyPackage}}/api/deps.py
```python
from {{PyPackage}}.infrastructure.memory_repository import InMemoryItemRepository
from {{PyPackage}}.application.use_cases import ListItems, CreateItem

_repo = InMemoryItemRepository()

def list_items_uc() -> ListItems:
    return ListItems(_repo)

def create_item_uc() -> CreateItem:
    return CreateItem(_repo)
```

### file: {{PyPackage}}/api/main.py
```python
from fastapi import FastAPI, Depends
from fastapi.middleware.cors import CORSMiddleware
from {{PyPackage}}.domain.entities import Item
from {{PyPackage}}.application.use_cases import ListItems, CreateItem
from {{PyPackage}}.api.deps import list_items_uc, create_item_uc

app = FastAPI(title="{{ProjectName}}", version="1.0.0")
app.add_middleware(CORSMiddleware, allow_origins=["*"],
                   allow_credentials=True, allow_methods=["*"], allow_headers=["*"])

@app.get("/api/health")
def health():
    return {"status": "healthy"}

@app.get("/api/items")
def list_items(uc: ListItems = Depends(list_items_uc)):
    return uc()

@app.post("/api/items")
def create_item(item: Item, uc: CreateItem = Depends(create_item_uc)):
    return uc(item)
```
