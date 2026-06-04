# 👑 Master IR Architecture

The **Master IR Manifest** (`master_ir.json`) is the absolute source of truth for the Thunder Engine. It consolidates the high-level orchestration intent and the low-level implementation details into a single, unified structure.

## 📂 File Structure

### 1. `orchestration` (Quick Layer)
The high-level UI composition.
- **screens**: Screen slots and templates.
- **widgets**: UI types and data strategies.
- **navigation**: High-level routing graph.
- **models**: Data schema definitions.

### 2. `implementation` (Depth Layer)
The low-level compiler instructions.
- **screen_registry**: Metadata for screen generation (scaffold, feature mapping).
- **layout_depth**: Precise geometric properties for UI containers.
- **widget_registry**: Pre-configured reusable widget instances.
- **routing_depth**: Detailed transition types (push, replace, modal).
- **logic_conditions**: Evaluatable expressions for business logic.
- **state_management**: Mappings to BLoC/Cubit logic.
- **data_sources**: External API and database connection parameters.
- **theme_tokens**: Design tokens for the styling engine.

---

## ⚡ Use Cases
- **Full System Backup**: A single point of recovery for the entire project state.
- **Monolithic Configuration**: Allows developers to bypass multiple files and manage the IR in a single location.
- **Architectural Reference**: Provides a bird's-eye view of how the Noob intent maps to the Pro implementation.
