---
id: django
label: Django
language: Python
kind: Backend
version: 1.0.0
packageManager: pip
defaultPort: 8000
architectures: [flat, layered]
defaultArchitecture: flat
entry: manage.py
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "HTTP port",     default: 8000, type: int }
  - { key: DjangoProject, prompt: "Django project module", default: "config" }
dependencies:
  - Django>=5.0
  - djangorestframework>=3.15
  - django-cors-headers>=4.3
provides: [API_BASE_URL]
needs: [DATABASE_URL]
postInstall: "pip install -r requirements.txt && python manage.py migrate"
run: "python manage.py runserver 0.0.0.0:{{Port}}"
tags: [api, python, django, rest]
---

# Django Template

A Django + DRF starter. **Flat** = single project + one app. **Layered** = adds a `services/`
business layer and a dedicated `api/` app, keeping views thin.

## Shared

### file: requirements.txt
```text
Django>=5.0
djangorestframework>=3.15
django-cors-headers>=4.3
```

### file: .env.example
```text
DJANGO_SECRET_KEY=change-me
DEBUG=1
PORT={{Port}}
DATABASE_URL=postgresql://postgres:postgres@localhost:5432/{{ProjectName}}
CORS_ORIGINS=http://localhost:3000
```

### file: .gitignore
```text
__pycache__/
*.pyc
.venv/
db.sqlite3
.env
.syncro_db/
```

### file: manage.py
```python
#!/usr/bin/env python
import os, sys

def main():
    os.environ.setdefault("DJANGO_SETTINGS_MODULE", "{{DjangoProject}}.settings")
    from django.core.management import execute_from_command_line
    execute_from_command_line(sys.argv)

if __name__ == "__main__":
    main()
```

### file: {{DjangoProject}}/__init__.py
```python
```

### file: {{DjangoProject}}/settings.py
```python
from pathlib import Path
import os

BASE_DIR = Path(__file__).resolve().parent.parent
SECRET_KEY = os.environ.get("DJANGO_SECRET_KEY", "change-me")
DEBUG = os.environ.get("DEBUG", "1") == "1"
ALLOWED_HOSTS = ["*"]

INSTALLED_APPS = [
    "django.contrib.contenttypes",
    "django.contrib.auth",
    "rest_framework",
    "corsheaders",
    "api",
]

MIDDLEWARE = [
    "corsheaders.middleware.CorsMiddleware",
    "django.middleware.common.CommonMiddleware",
]

ROOT_URLCONF = "{{DjangoProject}}.urls"
CORS_ALLOW_ALL_ORIGINS = True

DATABASES = {
    "default": {"ENGINE": "django.db.backends.sqlite3", "NAME": BASE_DIR / "db.sqlite3"}
}
DEFAULT_AUTO_FIELD = "django.db.models.BigAutoField"
```

### file: {{DjangoProject}}/urls.py
```python
from django.urls import path, include

urlpatterns = [
    path("api/", include("api.urls")),
]
```

### file: {{DjangoProject}}/wsgi.py
```python
import os
from django.core.wsgi import get_wsgi_application
os.environ.setdefault("DJANGO_SETTINGS_MODULE", "{{DjangoProject}}.settings")
application = get_wsgi_application()
```

## Architecture: flat

### file: api/__init__.py
```python
```

### file: api/views.py
```python
from rest_framework.decorators import api_view
from rest_framework.response import Response

@api_view(["GET"])
def health(request):
    return Response({"status": "healthy"})

@api_view(["GET"])
def root(request):
    return Response({"message": "Welcome to {{ProjectName}}", "status": "online"})
```

### file: api/urls.py
```python
from django.urls import path
from . import views

urlpatterns = [
    path("", views.root),
    path("health/", views.health),
]
```

## Architecture: layered

### file: api/__init__.py
```python
```

### file: api/services/item_service.py
```python
"""Business logic layer — views stay thin and delegate here."""

class ItemService:
    def __init__(self) -> None:
        self._items: list[dict] = []

    def list_items(self) -> list[dict]:
        return self._items

    def create_item(self, name: str) -> dict:
        item = {"id": len(self._items) + 1, "name": name}
        self._items.append(item)
        return item

item_service = ItemService()
```

### file: api/views.py
```python
from rest_framework.decorators import api_view
from rest_framework.response import Response
from api.services.item_service import item_service

@api_view(["GET"])
def health(request):
    return Response({"status": "healthy"})

@api_view(["GET", "POST"])
def items(request):
    if request.method == "POST":
        created = item_service.create_item(request.data.get("name", ""))
        return Response(created, status=201)
    return Response(item_service.list_items())
```

### file: api/urls.py
```python
from django.urls import path
from . import views

urlpatterns = [
    path("health/", views.health),
    path("items/", views.items),
]
```
