---
id: flask
label: Flask
language: Python
kind: Backend
version: 1.0.0
packageManager: pip
defaultPort: 5000
architectures: [flat]
defaultArchitecture: flat
placeholders:
  - { key: ProjectName, prompt: "Project name", default: "myapp" }
  - { key: Port,        prompt: "HTTP port",     default: 5000, type: int }
provides: [API_BASE_URL]
needs: [DATABASE_URL]
run: "flask run --port {{Port}}"
tags: [api, python, flask]
---

# Flask Template

A minimal but real Flask API with health and items endpoints.

## Shared

### file: README.md
```markdown
# {{ProjectName}}

Flask service scaffolded by Syncro.

## Run
```
pip install -r requirements.txt
python app.py
```
Open http://localhost:{{Port}}/api/health
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

### file: .env.example
```text
APP_NAME={{ProjectName}}
PORT={{Port}}
DATABASE_URL=postgresql://postgres:postgres@localhost:5432/{{ProjectName}}
```

## Architecture: flat

### file: requirements.txt
```text
flask>=3.0.0
flask-cors>=4.0.0
python-dotenv>=1.0.0
```

### file: app.py
```python
import os
from flask import Flask, jsonify, request
from flask_cors import CORS

app = Flask("{{ProjectName}}")
CORS(app)

_items = []


@app.get("/")
def root():
    return jsonify(message="Welcome to {{ProjectName}}", status="online")


@app.get("/api/health")
def health():
    return jsonify(status="healthy")


@app.get("/api/items")
def list_items():
    return jsonify(_items)


@app.post("/api/items")
def create_item():
    item = request.get_json(force=True) or {}
    item["id"] = len(_items) + 1
    _items.append(item)
    return jsonify(item), 201


if __name__ == "__main__":
    port = int(os.environ.get("PORT", {{Port}}))
    app.run(host="0.0.0.0", port=port, debug=True)
```
