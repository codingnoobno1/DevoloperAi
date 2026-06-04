# 📡 Navigation Schema (Custom Config)

The deep routing state machine and transition definitions.

## Structure
```json
{
  "navigation": {
    "entry": "string",       // Entry screen ID
    "transitions": [
      {
        "id": "string",      // Unique transition ID
        "type": "string",    // Transition type: push | replace | modal
        "target": "string"   // Destination screen ID
      }
    ]
  }
}
```
