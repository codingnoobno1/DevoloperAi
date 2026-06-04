#!/bin/bash
echo "Syncro: Installing kubectl..."
sudo -S apt-get update
sudo -S apt-get install -y apt-transport-https ca-certificates curl
curl -fsSL https://pkgs.k8s.io/core:/stable:/v1.28/deb/Release.key | sudo -S gpg --dearmor -o /etc/apt/keyrings/kubernetes-apt-keyring.gpg
echo 'deb [signed-by=/etc/apt/keyrings/kubernetes-apt-keyring.gpg] https://pkgs.k8s.io/core:/stable:/v1.28/deb/ /' | sudo -S tee /etc/apt/sources.list.d/kubernetes.list
sudo -S apt-get update
sudo -S apt-get install -y kubectl
echo "✅ kubectl installed."
