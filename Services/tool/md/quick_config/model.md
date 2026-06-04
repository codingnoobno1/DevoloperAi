# 📦 Model Schema (Quick Config)

Defines data schemas for typed AOT code generation.

## Structure
```json
{
  "models": [
    {
      "id": "string",       // Unique model identifier (e.g. userProfile)
      "fields": {
        "key": "string"     // Field name mapped to data type (string, number, datetime)
      }
    }
  ]
}
```
