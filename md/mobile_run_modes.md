# Screen: Mobile Run Selector

## Option A: ADB (USB Phone)
- **Status Check**: Verifies if a device is connected via `adb devices`.
- **System Impact**: Low.
- **Requirement**: 30+ GB storage recommended for dev tools.

## Option B: Emulator (Native)
- **Warning Overlay**: "High System Usage Detected".
- **Requirements**:
  - `RAM`: 6GB Minimum.
  - `Storage`: 50+ GB.
  - `CPU`: 6 Core Minimum.
- **Action**: Launch AVD Manager or specific emulator instance.

## Option C: Syncro Quick (Web Preview)
- **Process**: Runs `flutter build web` and hosts the output locally.
- **UX**: Opens a mobile-framed `BlazorWebView` inside Syncro Desktop.
- **Benefit**: Zero-latency preview for UI/UX development.

---
*Status: Planned*
