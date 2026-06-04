import express from "express";
import dotenv from "dotenv";
import fetch from "node-fetch";
import cors from "cors";

dotenv.config();

const app = express();
app.use(cors());
app.use(express.json());

const PORT = process.env.PORT || 3020;
const GEMINI_API_KEY = process.env.GEMINI_API_KEY;

// Test route
app.get("/", (req, res) => {
  res.json({ message: "Gemini Express server running 🚀" });
});

// Gemini route
app.post("/gemini", async (req, res) => {
  try {
    const { prompt, aiMode, workspacePath, model } = req.body;
    if (!prompt) return res.status(400).json({ error: "Missing prompt" });

    const selectedModel = model || "gemini-2.5-flash";
    let geminiPrompt = prompt;

    if (aiMode === "BatchFileGenerator") {
      geminiPrompt = `You are an expert Windows batch script generator. Your ONLY task is to generate executable batch scripts based on the following user request: "${prompt}". The script should create necessary file structures and output information about the created files/paths. The script should be saved in the specified workspace path: "${workspacePath}".

CRITICAL RULES:
1. Generate ONLY the batch script content - NO conversational text, NO markdown, NO comments outside the script.
2. Start immediately with @echo off or the first batch command.
3. Do NOT include phrases like 'Here is the script:', 'Generated script:', or any explanatory text.
4. Do NOT use code blocks, backticks, or markdown formatting.
5. The output must be a pure, executable Windows batch file (.bat) that can be saved directly to a .bat file.
6. Absolutely DO NOT include commands or syntax for Linux/macOS (e.g., #!, bash, sh, chmod, apt-get, brew, etc.). Only use Windows batch commands.
7. The generated batch script should, at the end, print the full path(s) of any created files or directories to the console (using 'echo').

Additionally, include commands to create any necessary folder structure based on the request.
`;
    } else if (aiMode === "EnvironmentSetup") {
      geminiPrompt = `You are an expert in setting up development environments for various languages on Windows. Your ONLY task is to generate a Windows batch script or a sequence of Windows commands to set up the environment for the following request: "${prompt}". The environment should be set up in the specified workspace path: "${workspacePath}". Include commands to create the folder structure and install necessary dependencies for the language/framework requested. 

CRITICAL RULES:
1. Generate ONLY the script content (Windows batch commands) - NO explanations, NO markdown, NO comments outside the script
2. Start immediately with @echo off or the first batch command
3. Do NOT include phrases like 'Here is the script:', 'Generated script:', or any explanatory text
4. Do NOT use code blocks, backticks, or markdown formatting
5. The output must be pure, executable Windows commands that can be run directly in a .bat file.
6. Absolutely DO NOT include commands or syntax for Linux/macOS (e.g., #!, bash, sh, chmod, apt-get, brew, etc.). Only use Windows batch commands.

Provide commands for environment setup and folder structure creation for the specified language/framework.
`;
    } else if (aiMode === "FlutterProject") {
      geminiPrompt = `You are an expert Flutter developer and Windows automation specialist. Your ONLY task is to generate a Windows batch script that scaffolds a complete Flutter project and creates a rich UI based on the following request: "${prompt}". The project should be created in the specified workspace path: "${workspacePath}".

CRITICAL RULES:
1. Generate ONLY the batch script content.
2. The script should use the Flutter CLI (e.g., 'flutter create .').
3. Include commands to overwrite lib/main.dart and create other necessary folders/files (like features, models, etc.) with functional code.
4. Use 'echo' to output the progress.
5. NO conversational text, NO markdown.
6. The output must be a pure .bat file.
7. Start with @echo off.
`;
    } else if (aiMode === "FlutterSandbox") {
      geminiPrompt = `You are an expert Flutter developer and Windows automation specialist. Your ONLY task is to generate Dart code for a Flutter app based on the following request: "${prompt}".

CRITICAL RULES:
1. You MUST respond with a VALID JSON object ONLY.
2. The JSON object MUST follow this EXACT schema:
{
  "explanation": "Brief summary of what you did",
  "changes": [
    {
      "type": "create" | "modify" | "delete",
      "path": "relative/path/to/file.dart",
      "content": "Full content of the file"
    }
  ]
}
3. NO conversational text, NO markdown, NO explanations outside the JSON.
4. Do NOT use semicolons (;) or any other characters outside of string values in the JSON structure. 
5. Ensure all Dart code is properly escaped as a string within the "content" field.
6. The Dart code must be production-ready and include all necessary imports.

Example of a PERFECT response:
{
  "explanation": "Added a bottom navigation bar with 3 items.",
  "changes": [
    {
      "type": "modify",
      "path": "lib/main.dart",
      "content": "import 'package:flutter/material.dart';\n\nvoid main() => runApp(const MyApp());..."
    }
  ]
}

DO NOT return anything else. DO NOT return partial JSON. DO NOT return CSS.
`;
    }

    const response = await fetch(
      `https://generativelanguage.googleapis.com/v1beta/models/${selectedModel}:generateContent?key=${GEMINI_API_KEY}`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          contents: [{ parts: [{ text: geminiPrompt }] }],
        }),
      }
    );

    const data = await response.json();

    // If the API returned a non-OK status, forward a helpful error to the client
    if (!response.ok) {
      return res.status(response.status).json({
        success: false,
        error: data.error || data,
        message:
          `Generative API returned an error for model ${selectedModel} (see \`error\`). Call GET /models to list available models and supported methods.`,
      });
    }

    // Return Gemini response as JSON
    res.json({
      success: true,
      model: selectedModel,
      result: data,
    });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error" });
  }
});

app.listen(PORT, () => {
  console.log(`✅ Server running on http://localhost:${PORT}`);
});

// List available models (useful for debugging model names / supported methods)
app.get("/models", async (req, res) => {
  try {
    const response = await fetch(
      `https://generativelanguage.googleapis.com/v1beta/models?key=${GEMINI_API_KEY}`
    );
    const data = await response.json();

    if (!response.ok) {
      return res.status(response.status).json({ success: false, error: data });
    }

    // `data` may contain a `models` array or similar structure depending on API
    res.json({ success: true, models: data.models || data });
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: "Server error" });
  }
});
