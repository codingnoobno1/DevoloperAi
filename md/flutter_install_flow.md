# Screen: Flutter Automated Installation

## Detection Logic
- **Trigger**: When "Mobile" project is selected and `flutter` is missing from environment.
- **Action**: Show "Flutter Not Found" overlay with "Install Now" button.

## Installation Sub-Screen
- **Requirements Check**:
  - `Disk Space`: 5GB Minimum (Live progress bar showing available vs required).
  - `Permissions`: User must accept elevated privileges.
- **Progress View**:
  - `Syncro Terminal Animation`: A spinning holographic globe or terminal stream.
  - `Status Text`: "Downloading SDK...", "Extracting binaries...", "Configuring PATH...".
- **Finalization**:
  - Opens a dedicated PowerShell window to run `flutter doctor --android-licenses`.
  - "Success" checkmark once the environment is detected.

---
*Status: Planned*
