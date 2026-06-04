# 🧩 Widgets Schema (Quick Config)

Defines UI components, their templates, and their data binding strategies.

## Structure
```json
{
  "widgets": [
    {
      "id": "string",       // Unique identifier for the widget
      "type": "string",     // Flutter component type (map, card, list, button)
      "template": "string", // Visual variant (promo_card, full_map, default)
      "data": {
        "type": "string",   // Data strategy: static | api | model
        "endpoint": "string", // (Optional) API path for 'api' type
        "model": "string",    // (Optional) Model schema ID
        "name": "string",     // (Optional) Local state/model name
        "value": {}           // (Optional) Hardcoded data for 'static' type
      },
      "action": {           // (Optional) Interactive behavior
        "type": "string",   // Action type (navigate, call_api)
        "target": "string"  // Navigation target or API ID
      }
    }
  ]
}
```
