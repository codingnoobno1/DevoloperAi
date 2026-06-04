# 🧠 Condition Schema (Custom Config)

Centralizes business logic conditions and branching rules.

## Structure
```json
{
  "condition": {
    "conditionId": {        // Unique condition identifier (e.g. isLoggedIn)
      "expression": "string", // Logic expression to evaluate
      "true": "any",        // Value/Action if true
      "false": "any"        // Value/Action if false
    }
  }
}
```
