#!/bin/bash
echo "Syncro: Installing CMake..."
sudo -S apt-get update
sudo -S apt-get install -y cmake
cmake --version
echo "✅ CMake installed."
