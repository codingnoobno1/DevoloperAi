#!/bin/bash
echo "Syncro: Checking Rust on Linux..."

if command -v rustc &> /dev/null
then
    echo "✅ Rust found: $(rustc --version)"
else
    echo "🚀 Installing Rust via rustup.rs..."
    curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y
    echo "✅ Rust installed. Please source $HOME/.cargo/env"
fi
