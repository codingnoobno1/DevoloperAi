#!/bin/bash
# setup-venv.sh — Syncro Python virtual environment bootstrapper

set -e

PYTHON=$(which python3 2>/dev/null || which python 2>/dev/null)
if [ -z "$PYTHON" ]; then
  echo "ERROR: Python not found. Install Python 3.8+ first."
  exit 1
fi

echo "Python found: $($PYTHON --version)"

if [ ! -d ".venv" ]; then
  echo "Creating virtual environment (.venv)..."
  $PYTHON -m venv .venv
  echo "Virtual environment created."
else
  echo ".venv already exists — skipping creation."
fi

# Activate and install deps
if [ -f ".venv/bin/activate" ]; then
  source .venv/bin/activate
elif [ -f ".venv/Scripts/activate" ]; then
  source .venv/Scripts/activate
fi

if [ -f "requirements.txt" ]; then
  echo "Installing requirements.txt..."
  pip install -r requirements.txt -q
  echo "Dependencies installed."
elif [ -f "pyproject.toml" ]; then
  echo "Installing pyproject.toml..."
  pip install -e . -q
  echo "Dependencies installed."
else
  echo "No requirements.txt or pyproject.toml found. Venv ready but no deps installed."
fi

echo ""
echo "✅ Python environment ready."
echo "   Activate: source .venv/bin/activate  (Linux/Mac)"
echo "   Activate: .venv\\Scripts\\Activate.ps1  (Windows PowerShell)"
