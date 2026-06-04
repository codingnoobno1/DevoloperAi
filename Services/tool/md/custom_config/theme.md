# 🎨 Theme Schema (Custom Config)

The design system manifest containing all design tokens.

## Structure
```json
{
  "theme": {
    "light": {              // Light Mode tokens
      "primary": "string",  // Hex color
      "secondary": "string",
      "background": "string",
      "surface": "string",
      "text": "string"
    },
    "dark": {               // Dark Mode tokens (Optional)
      "primary": "string"
    },
    "spacing": {            // Spacing tokens
      "page": number,
      "widget": number
    }
  }
}
```
