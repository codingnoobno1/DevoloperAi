# 📱 Screens Schema (Quick Config)

Defines the top-level composition of app screens using templates and slots.

## Structure
```json
{
  "screens": [
    {
      "id": "string",          // Unique identifier for the screen
      "layout": {
        "type": "string",      // Layout type (vertical, horizontal, stack, tabs)
        "template": "string",  // Predefined template name (header_body, default_stack)
        "slots": [             // Ordered list of component slots
          { 
            "id": "string",    // ID of the widget to place in this slot
            "position": number // Deterministic render order
          }
        ]
      },
      "data": {
        "type": "string"       // Screen-level data binding type (static, api)
      }
    }
  ]
}
```
