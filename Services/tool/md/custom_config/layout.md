# 🧱 Layout Schema (Custom Config)

Defines granular geometric and structural properties for UI containers.

## Structure
```json
{
  "layouts": {
    "layoutId": {           // Unique layout identifier
      "type": "string",     // Layout type: vertical | horizontal | stack | tabs
      "properties": {
        "spacing": number,  // Space between children
        "padding": [number], // [left, top, right, bottom]
        "alignment": "string" // alignment: center | start | end
      }
    }
  }
}
```
