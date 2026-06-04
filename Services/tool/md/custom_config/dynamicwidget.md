# 🧩 DynamicWidget Schema (Custom Config)

Registry of reusable, pre-configured widget instances.

## Structure
```json
{
  "dynamicWidgets": [
    {
      "id": "string",       // Unique widget instance ID
      "type": "string",     // Component type (map, image, button, etc.)
      "properties": {       // Component-specific properties
        "key": "any"
      }
    }
  ]
}
```
