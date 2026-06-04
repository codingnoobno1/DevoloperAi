# 🏛️ Screentype Schema (Custom Config)

Registers screens and maps them to features and scaffold configurations.

## Structure
```json
{
  "screenTypes": [
    {
      "id": "string",       // Unique screen identifier
      "feature": "string",   // Feature folder name for code generation
      "scaffold": boolean,  // Whether to wrap the screen in a Scaffold
      "safeArea": boolean   // Whether to wrap the screen in a SafeArea
    }
  ]
}
```
