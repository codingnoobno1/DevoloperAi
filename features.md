# Syncro.Desktop - Project Features

Syncro.Desktop is an AI-powered developer assistant built with .NET 9 and MAUI, designed to streamline project initialization, environment setup, and development workflows.

## 🤖 AI Agent Assistant
The core of the application is a versatile AI agent that can process natural language prompts to perform various development tasks.
*   **Automatic Mode Detection**: Uses NLP services to identify the user's intent and select the appropriate AI mode.
*   **Multiple AI Modes**:
    *   **General Chat**: For coding questions and general assistance.
    *   **Batch File Generator**: Creates executable scripts for automation.
    *   **Environment Setup**: Generates and executes setup scripts for development tools.
    *   **Project Generators**: Specialized modes for various tech stacks.
    *   **Documentation Generator**: Automatically creates project documentation based on prompts.
*   **Direct Execution**: Capability to run generated scripts directly within the application's workspace.

## 🚀 Multi-Stack Project Generators
Syncro.Desktop can bootstrap complete project structures for several popular technology stacks:
*   **MERN Stack**: Node.js (Express), React, and MongoDB boilerplate.
*   **Spring Boot**: Java-based enterprise application setup.
*   **PHP**: Basic PHP project structures.
*   **.NET**: Modern .NET project initialization.

## 🛠️ Quick Start Tools
One-click tools for common development tasks:
*   **Python Venv + Flask**: Installs and configures a Python virtual environment with Flask.
*   **Java Maven Project**: Sets up a Maven-based Java project with custom Group and Artifact IDs.
*   **Package Manager Setup**: Automated installation of **Chocolatey** and **Scoop**.

## 📊 Environment Management
*   **Automatic Detection**: Scans the local machine for installed development environments.
*   **Live Dashboard**: Displays the name, version, path, and availability status of tools like Node.js, Python, Java, and .NET.

## 📂 Workspace & Terminal Integration
*   **Native Folder Picker**: Integrated file system access for selecting project directories.
*   **Automated Terminal**: Option to open a terminal in the target workspace automatically after project creation.
*   **Initial Command Execution**: Pre-configure the terminal to run commands like `npm start` or `python main.py` on launch.
*   **Clipboard Management**: Quick access to copy AI-generated scripts.

## 🌐 Modern Desktop Architecture
*   **MAUI & .NET 9**: Leveraging the latest .NET features for high performance.
*   **Blazor Hybrid UI**: Combines the power of native desktop apps with the flexibility of Blazor web technologies.
*   **Single-Target Optimization**: Optimized specifically for Windows desktop environments.

## 🐙 Git Integration
*   **Git Manager**: Integrated services for tracking project status and managing Git repositories.
