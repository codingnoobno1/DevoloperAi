# 📦 Model Schema (Custom Config)

The deep-layer schema for entity modeling and code generation.

## Structure
```json
{
  "models": [
    {
      "id": "string",       // Unique model ID
      "fields": {           // Deep field definitions
        "name": "string",   // string | number | boolean | datetime | map | list
        "type": "string"
      }
    }
  ]
}
```
