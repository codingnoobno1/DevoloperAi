#!/bin/bash
echo "Syncro: Checking Python 3 on Linux..."

if command -v python3 &> /dev/null
then
    echo "✅ Python 3 found: $(python3 --version)"
else
    echo "❌ Python 3 not found. Installing via apt..."
    sudo apt update && sudo apt install -y python3 python3-pip
    echo "✅ Python 3 installed."
fi
