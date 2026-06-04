# Screen: Project Creation Wizard

## Step 1: Project Type Selection
**Purpose**: Allow the user to choose the high-level category of their project.
- **UI Elements**: 
  - Grid of high-density cards (Web, Mobile, Desktop, API).
  - Hover animations with scale and glow effects.
  - Hover descriptions showing recommended tech stacks.

## Step 2: Specific Sub-Screens
**Purpose**: Collect type-specific metadata.
- **Mobile (Flutter)**: 
  - Input for `Package Name` (com.example.app).
  - Checkbox for `Platform Support` (Android, iOS, Web).
- **Web (Next.js/React)**:
  - Select `Template` (Dashboard, Landing, Blank).
  - Enable `TailwindCSS` toggle.

## Step 3: Dynamic Path & Agent Storage
**Purpose**: Define the project location and sync with Syncro Agent.
- **UI Elements**:
  - `Path Input`: Auto-generated based on project name.
  - `Store in Agent`: Toggle to automatically index the project for AI assistance.
  - `Summary`: Confirmation card showing all selected settings.

---
*Status: Planned*
