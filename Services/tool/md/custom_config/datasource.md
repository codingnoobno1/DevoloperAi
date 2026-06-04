# 🌐 Datasource Schema (Custom Config)

Defines the connection parameters for external APIs and databases.

## Structure
```json
{
  "datasource": {
    "type": "string",       // api | database
    "api": {                // REST API Configuration
      "baseUrl": "string",
      "headers": {},
      "timeout": number
    },
    "database": {           // Local DB Configuration
      "name": "string",
      "version": number
    }
  }
}
```
