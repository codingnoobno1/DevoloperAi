<<<<<<< HEAD
# DeveloperAI - Advanced Script Generator

A powerful WPF application that uses AI to generate comprehensive batch scripts for development tasks, with advanced features for environment management, terminal control, and project setup.

## 🚀 Features

### Core Functionality
- **AI-Powered Script Generation**: Uses Groq's Llama3-70B model to generate intelligent batch scripts
- **Context-Aware Generation**: Considers working directory, environment, and port configuration
- **Multi-Environment Support**: Python, Node.js, Java, C#/.NET, Go, Rust

### Advanced Features

#### 📁 Working Directory Management
- Browse and select working directories
- Automatic directory validation
- Context-aware script generation based on selected path

#### 🐍 Environment Management
- **Environment Detection**: Automatically detects installed development environments
- **Environment Setup**: Generate setup scripts for different programming languages
- **Library Installation**: Install common libraries and dependencies for each environment
- **Virtual Environment Support**: Python virtual environments, Node.js package management

#### 🔌 Port Management
- **Port Availability Check**: Verify if ports are available before use
- **Port Configuration**: Specify ports for web servers and applications
- **Conflict Resolution**: Handle port conflicts automatically

#### 💻 Terminal Integration
- **Open Terminal**: Launch command prompt at specific locations
- **Script Execution**: Run generated scripts with real-time output
- **Error Handling**: Capture and display script output and errors

#### 📦 Library Installation
- **Python**: Flask, requests, numpy, pandas, matplotlib
- **Node.js**: Express, axios, nodemon, cors
- **Java**: Maven project structure, pom.xml generation
- **.NET**: Web API project creation
- **Go**: Module initialization, Gin framework
- **Rust**: Cargo project setup

## 🛠️ Installation

### Prerequisites
- Windows 10/11
- .NET 9.0 Runtime
- Groq API Key (optional, fallback key provided)

### Setup
1. Clone or download the project
2. Open `DeveloperAI.sln` in Visual Studio or your preferred IDE
3. Build the solution
4. Run the application

### API Key Configuration
Set your Groq API key as an environment variable:
```bash
set GROQ_API_KEY=your_api_key_here
```

## 📖 Usage

### Basic Script Generation
1. Enter your prompt describing what you want to accomplish
2. Select your working directory
3. Choose your development environment
4. Configure port settings if needed
5. Click "Generate Script"

### Environment Setup
1. Click "Check Environments" to see what's installed
2. Select your target environment from the dropdown
3. Click "Install Libraries" to set up dependencies
4. Use "Open Terminal" to work in your project directory

### Advanced Workflows

#### Python Web Development
```
Prompt: "Create a Flask web server with REST API endpoints"
Environment: Python
Port: 5000
```

#### Node.js Application
```
Prompt: "Set up an Express server with MongoDB connection"
Environment: Node.js
Port: 3000
```

#### Java Spring Boot
```
Prompt: "Create a Spring Boot application with JPA"
Environment: Java
Port: 8080
```

## 🔧 Technical Details

### Architecture
- **WPF Application**: Modern Windows desktop interface
- **Async/Await**: Non-blocking UI operations
- **Environment Detection**: Automatic toolchain discovery
- **Error Handling**: Comprehensive error management
- **Security**: Input validation and safe script execution

### Supported Environments

#### Python
- Virtual environment creation
- pip package management
- Common web frameworks (Flask, Django)
- Data science libraries (numpy, pandas)

#### Node.js
- npm package management
- Express.js framework
- Development tools (nodemon)
- Common middleware (cors, body-parser)

#### Java
- Maven project structure
- Spring Boot setup
- JPA/Hibernate configuration
- REST API development

#### .NET
- Web API project creation
- Entity Framework setup
- Dependency injection
- Swagger documentation

#### Go
- Module initialization
- Gin web framework
- Database drivers
- Testing setup

#### Rust
- Cargo project creation
- Web framework setup (actix-web)
- Database integration
- Error handling patterns

## 🎯 Example Scripts

### Python Flask Server
```batch
@echo off
echo Starting Python Flask Development Server...
cd /d "C:\Projects\myapp"

REM Check if Python is available
python --version >nul 2>&1
if errorlevel 1 (
    echo Error: Python not found
    pause
    exit /b 1
)

REM Activate virtual environment if exists
if exist venv\Scripts\activate.bat (
    call venv\Scripts\activate.bat
)

REM Install Flask if not installed
python -c "import flask" >nul 2>&1
if errorlevel 1 (
    echo Installing Flask...
    pip install flask
)

REM Start Flask server
echo Starting Flask server on port 5000...
python -m flask run --host=0.0.0.0 --port=5000
```

### Node.js Express Server
```batch
@echo off
echo Starting Node.js Express Server...
cd /d "C:\Projects\myapp"

REM Check if Node.js is available
node --version >nul 2>&1
if errorlevel 1 (
    echo Error: Node.js not found
    pause
    exit /b 1
)

REM Install dependencies if package.json exists
if exist package.json (
    echo Installing dependencies...
    npm install
)

REM Start Express server
echo Starting Express server on port 3000...
node app.js
```

## 🔒 Security Considerations

- **Input Validation**: All user inputs are validated
- **Script Safety**: Generated scripts include error checking
- **API Key Management**: Use environment variables for API keys
- **Execution Control**: User confirmation before script execution

## 🐛 Troubleshooting

### Common Issues

#### Environment Not Detected
- Ensure the development tool is installed and in PATH
- Restart the application after installing new tools
- Use "Check Environments" to verify installation

#### Port Already in Use
- Use "Check Port" to verify availability
- Change port number in configuration
- Kill processes using the port if necessary

#### Script Execution Errors
- Check working directory exists
- Verify environment is properly installed
- Review error messages in the output display

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Test thoroughly
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Acknowledgments

- Groq for providing the LLM API
- Microsoft for .NET and WPF frameworks
- The open-source community for inspiration and tools

---

**DeveloperAI** - Making development automation intelligent and accessible! 🚀 
=======
# DevoloperAi
>>>>>>> cd76df23d5299a2f7caf95ff878b331374308692
