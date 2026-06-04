#!/bin/bash
echo "Syncro: Checking Java on Linux..."

if command -v java &> /dev/null
then
    echo "✅ Java found: $(java -version 2>&1 | head -n 1)"
else
    echo "❌ JDK not found. Installing OpenJDK 21..."
    sudo apt update && sudo apt install -y openjdk-21-jdk
    echo "✅ JDK 21 installed."
fi
