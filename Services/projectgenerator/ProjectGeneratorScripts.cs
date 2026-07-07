using System;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.projectgenerator
{
    public static class ProjectGeneratorScripts
    {
        public static string GetFastApiMainPy() => @"from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

app = FastAPI(title=""Syncro Backend API"", version=""1.0.0"")

app.add_middleware(
    CORSMiddleware,
    allow_origins=[""*""],
    allow_credentials=True,
    allow_methods=[""*""],
    allow_headers=[""*""],
)

@app.get(""/"")
def read_root():
    return {""message"": ""Welcome to Syncro FastAPI Service!"", ""status"": ""Online""}

@app.get(""/api/health"")
def health_check():
    return {""status"": ""healthy"", ""uptime"": ""100%""}

if __name__ == ""__main__"":
    uvicorn.run(""main:app"", host=""0.0.0.0"", port=8000, reload=True)
";

        public static string GetFastApiRequirements() => @"fastapi>=0.110.0
uvicorn>=0.28.0
pydantic>=2.6.0
";

        public static string GetFlaskAppPy() => @"from flask import Flask, jsonify
from flask_cors import CORS

app = Flask(__name__)
CORS(app)

@app.route(""/"")
def hello():
    return jsonify(message=""Welcome to Syncro Flask Service!"", status=""Online"")

@app.route(""/api/health"")
def health():
    return jsonify(status=""healthy"")

if __name__ == ""__main__"":
    app.run(host=""0.0.0.0"", port=5000, debug=True)
";

        public static string GetFlaskRequirements() => @"Flask>=3.0.0
flask-cors>=4.0.0
";

        public static string GetExpressServerJs() => @"const express = require('express');
const cors = require('cors');
const app = express();
const PORT = process.env.PORT || 5000;

app.use(cors());
app.use(express.json());

app.get('/', (req, res) => {
    res.json({ message: 'Welcome to Syncro Express Service!', status: 'Online' });
});

app.get('/api/health', (req, res) => {
    res.json({ status: 'healthy' });
});

app.listen(PORT, () => {
    console.log(`Server running on port ${PORT}`);
});
";

        public static string GetExpressPackageJson(string name) => $@"{{
  ""name"": ""{name}"",
  ""version"": ""1.0.0"",
  ""description"": ""Syncro scaffolded Express Backend"",
  ""main"": ""server.js"",
  ""scripts"": {{
    ""start"": ""node server.js"",
    ""dev"": ""node server.js""
  }},
  ""dependencies"": {{
    ""express"": ""^4.19.0"",
    ""cors"": ""^2.8.5""
  }}
}}";

        public static string GetSpringBootBuildGradle() => @"plugins {
    id 'java'
    id 'org.springframework.boot' version '3.2.4'
    id 'io.spring.dependency-management' version '1.1.4'
}

group = 'com.example'
version = '0.0.1-SNAPSHOT'
sourceCompatibility = '17'

repositories {
    mavenCentral()
}

dependencies {
    implementation 'org.springframework.boot:spring-boot-starter-web'
    testImplementation 'org.springframework.boot:spring-boot-starter-test'
}

tasks.named('test') {
    useJUnitPlatform()
}
";

        public static string GetSpringBootApplicationJava() => @"package com.example.demo;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;
import java.util.HashMap;
import java.util.Map;

@SpringBootApplication
@RestController
public class DemoApplication {

    public static void main(String[] args) {
        SpringApplication.run(DemoApplication.class, args);
    }

    @GetMapping(""/"")
    public Map<String, String> home() {
        var res = new HashMap<String, String>();
        res.put(""message"", ""Welcome to Spring Boot Web Service!"");
        res.put(""status"", ""Online"");
        return res;
    }
}
";

        public static string GetNextJsPackageJson(string name) => $@"{{
  ""name"": ""{name}"",
  ""version"": ""0.1.0"",
  ""private"": true,
  ""scripts"": {{
    ""dev"": ""next dev"",
    ""build"": ""next build"",
    ""start"": ""next start""
  }},
  ""dependencies"": {{
    ""next"": ""14.1.4"",
    ""react"": ""^18.2.0"",
    ""react-dom"": ""^18.2.0""
  }}
}}";

        public static string GetNextJsAppLayout() => @"import './globals.css'

export const metadata = {
  title: 'Syncro Web App',
  description: 'Scaffolded by Syncro Project Generator',
}

export default function RootLayout({ children }) {
  return (
    <html lang=""en"">
      <body>{children}</body>
    </html>
  )
}
";

        public static string GetNextJsAppPage() => @"export default function Home() {
  return (
    <main style={{
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      minHeight: '100vh',
      fontFamily: 'sans-serif',
      backgroundColor: '#0f172a',
      color: '#f8fafc'
    }}>
      <h1 style={{ fontSize: '3rem', margin: '1rem', color: '#00f2fe' }}>SYNCRO PROJECT</h1>
      <p style={{ fontSize: '1.2rem', color: '#64748b' }}>Web Dashboard & Frontend Live Scaffolding</p>
    </main>
  )
}
";

        public static string GetNextJsGlobalsCss() => @"body {
  margin: 0;
  padding: 0;
  box-sizing: border-box;
}
";

        public static string GetSetupWindowsBat(string devCommand) => $@"@echo off
echo =========================================
echo Syncro Project Initialization Script
echo =========================================
echo Installing local environment dependencies...
{devCommand}
pause
";

        public static string GetSetupBashSh(string devCommand) => $@"#!/bin/bash
echo ""=========================================""
echo ""Syncro Project Initialization Script""
echo ""=========================================""
echo ""Installing local environment dependencies...""
{devCommand}
";
    }
}
