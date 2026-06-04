# Syncro.Desktop - UI Architecture

The UI architecture of Syncro.Desktop is built on a **Blazor Hybrid** model within the **.NET MAUI** framework. This approach combines the performance and native capabilities of a desktop application with the flexibility and rapid development cycle of web technologies.

## 🏗️ Architectural Overview

### 1. Host Framework: .NET MAUI
*   **Platform Host**: The application is hosted by .NET MAUI, which provides the native windowing, file system access, and system-level integrations.
*   **Native Windows**: The application uses native MAUI `ContentPage` containers (`LoginPage.xaml`, `MainPage.xaml`) to host the UI.
*   **Window Management**: Handled via `App.xaml.cs` using the standard MAUI windowing lifecycle (e.g., `CreateWindow`).

### 2. UI Engine: Blazor Hybrid
*   **BlazorWebView**: Each MAUI page contains a `BlazorWebView` control that serves as the bridge between the native host and the Blazor UI.
*   **Asset Hosting**: Web assets (HTML, CSS, JS) are served from the `wwwroot` directory.
*   **Direct Interop**: Blazor components run directly in the same process as the MAUI app, allowing for high-performance interaction between the UI and native C# services without any serialization overhead.

### 3. Component Hierarchy
The UI is organized into a modular component structure located in the `Components` directory:
*   **`Routes.razor`**: The central routing hub using the Blazor `<Router>` to map URLs to specific page components.
*   **`Pages/`**: Contains the top-level Blazor components that represent different views (e.g., `Home.razor`, `Login.razor`, `GitManager.razor`).
*   **`Layout/`**: Defines the visual shell and common navigation elements (e.g., `MainLayout.razor`).
*   **`Shared/`**: Reusable UI components used across multiple pages.

### 4. Navigation & State Flow
*   **MAUI Navigation**: Transitions between major application states (like Login to Dashboard) are handled by updating the MAUI `MainPage` or the active window's `Page` property.
*   **Blazor Navigation**: Internal transitions (like switching between Dashboard tabs) are handled by the Blazor `NavigationManager`.

### 5. Dependency Injection (DI)
*   **Centralized Registry**: Services are registered in `MauiProgram.cs` using the standard .NET Generic Host builder.
*   **Injection Model**: Services are injected into Blazor components using the `@inject` directive, enabling seamless access to business logic, AI clients, and system services.

## 🛠️ Tech Stack
*   **Host**: .NET MAUI (.NET 9)
*   **Frontend**: Blazor Hybrid (HTML5, CSS3, Razor)
*   **Styling**: CSS with support for modern web layouts.
*   **Interactions**: C# for logic, JavaScript interop for browser-specific APIs (via `IJSRuntime`).
