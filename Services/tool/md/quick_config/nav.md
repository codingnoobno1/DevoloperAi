# 🔗 Navigation Schema (Quick Config)

Defines the application entry point and high-level routing flow.

## Structure
```json
{
  "entry": "string",       // The ID of the screen to show on app launch
  "routes": [
    {
      "id": "string",      // Unique route ID
      "from": "string",    // Source screen ID
      "to": "string"       // Destination screen ID
    }
  ]
}
```
