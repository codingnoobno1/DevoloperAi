# 🧱 BLoC Mapping Schema (Custom Config)

Maps UI components and features to State Management logic.

## Structure
```json
{
  "bloc": {
    "mappings": {
      "screenId": "blocId"  // Map screen ID to its corresponding Cubit/Bloc ID
    }
  },
  "blocs": [                // (Optional) Detailed BLoC definitions
    {
      "id": "string",       // Unique BLoC identifier
      "type": "string",     // Type: cubit | bloc
      "feature": "string",  // Associated feature
      "state": "string",    // State class name
      "actions": [          // Handled actions/triggers
        { "id": "string", "trigger": "string" }
      ]
    }
  ]
}
```
