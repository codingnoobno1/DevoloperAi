#!/bin/bash
echo "Syncro: Installing Docker & Compose..."
# Use -S to read password from stdin
sudo -S apt-get update
sudo -S apt-get install -y ca-certificates curl gnupg lsb-release
sudo -S mkdir -p /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo -S gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" | sudo -S tee /etc/apt/sources.list.d/docker.list > /dev/null
sudo -S apt-get update
sudo -S apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo -S usermod -aG docker $USER
echo "✅ Docker installed. Please logout and login for group changes."
