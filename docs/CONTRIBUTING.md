# Contributing to VoxMemo

Thank you for your interest in contributing to VoxMemo. This document provides guidelines and instructions for contributing to the project.

## Table of Contents

- [Development Setup](#development-setup)
- [Project Structure](#project-structure)
- [Code Style](#code-style)
- [Adding a New AI Provider](#adding-a-new-ai-provider)
- [Adding a New Transcription Engine](#adding-a-new-transcription-engine)
- [Adding a New View](#adding-a-new-view)
- [Pull Request Guidelines](#pull-request-guidelines)
- [Testing Guidelines](#testing-guidelines)

---

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- A C# IDE such as Visual Studio 2022, JetBrains Rider, or VS Code with the C# Dev Kit extension
- Windows 10/11 (WASAPI audio capture is Windows-only)
- Git

### Optional (for AI features)

- [Ollama](https://ollama.com/) installed and running locally for local AI summarization
- An OpenAI API key (for OpenAI provider)
- An Anthropic API key (for Anthropic/Claude provider)

### Clone and Build

```bash
git clone https://github.com/your-org/VoxMemo.git
cd VoxMemo
dotnet restore
dotnet build
```

### Run

```bash
dotnet run
```

Or open `VoxMemo.sln` in your IDE and press F5 to run with debugging.

### Verify the Build

After building, confirm that:
1. The application window opens with the recording view.
2. The system tray icon appears.
3. No errors are printed to the console output.

---

## Project Structure

```
VoxMemo/
|-- Assets/                          # Application icons and static assets
|-- Models/
|   |-- Meeting.cs                   # Meeting, Transcript, Summary, AppSettings entities
|-- Resources/
|   |-- i18n/
|       |-- Strings.resx             # English UI strings
|       |-- Strings.vi.resx          # Vietnamese UI strings
|-- Services/
|   |-- AI/
|   |   |-- IAiProvider.cs           # AI provider interface + AiModel record
|   |   |-- OllamaProvider.cs        # Ollama implementation
|   |   |-- OpenAiProvider.cs        # OpenAI implementation
|   |   |-- AnthropicProvider.cs     # Anthropic/Claude implementation
|   |   |-- PromptTemplates.cs       # Prompt templates for summarization
|   |-- Audio/
|   |   |-- IAudioRecorder.cs        # Audio recorder interface
|   |   |-- AudioRecorderService.cs  # WASAPI-based recording (mic + system audio)
|   |   |-- AudioConverter.cs        # Audio format conversion utilities
|   |-- Database/
|   |   |-- AppDbContext.cs           # EF Core SQLite database context
|   |-- Transcription/
|       |-- ITranscriptionService.cs  # Transcription interface + result records
|       |-- WhisperTranscriptionService.cs  # Local Whisper.net implementation
|-- ViewModels/
|   |-- ViewModelBase.cs             # Base class (extends ObservableObject)
|   |-- MainWindowViewModel.cs       # Shell/navigation view model
|   |-- RecordingViewModel.cs        # Recording and live captions
|   |-- MeetingsViewModel.cs         # Meeting history, transcripts, summaries
|   |-- SettingsViewModel.cs         # App configuration
|-- Views/
|   |-- Controls/                    # Reusable custom controls
|   |-- MainWindow.axaml(.cs)        # Application shell with navigation
|   |-- RecordingView.axaml(.cs)     # Recording UI
|   |-- MeetingsView.axaml(.cs)      # Meeting history UI
|   |-- SettingsView.axaml(.cs)      # Settings UI
|-- ViewLocator.cs                   # Resolves ViewModels to Views
|-- App.axaml(.cs)                   # Application entry, theme, DI setup
|-- Program.cs                       # Main entry point
|-- VoxMemo.csproj                   # Project file
|-- VoxMemo.sln                      # Solution file
```

---

## Code Style

### General C# Conventions

- Use C# 12+ features where appropriate (file-scoped namespaces, primary constructors, collection expressions).
- Enable nullable reference types. All new code must be nullable-aware.
- Use `var` when the type is obvious from the right-hand side; use explicit types when it improves clarity.
- Prefer records for immutable data transfer objects (e.g., `AiModel`, `TranscriptionResult`).
- Use `string.Empty` instead of `""`.
- Follow standard .NET naming conventions:
  - `PascalCase` for public members, types, namespaces, and methods.
  - `camelCase` for local variables and parameters.
  - `_camelCase` for private fields.
  - `I` prefix for interfaces (e.g., `IAiProvider`).

### MVVM Pattern

VoxMemo follows the Model-View-ViewModel (MVVM) pattern using the CommunityToolkit.Mvvm source generators:

- **Models** (`Models/`): Plain entity classes persisted via EF Core. No UI logic.
- **ViewModels** (`ViewModels/`): Inherit from `ViewModelBase` (which extends `ObservableObject`). Use `[ObservableProperty]`, `[RelayCommand]`, and other source-generated attributes from CommunityToolkit.Mvvm.
- **Views** (`Views/`): Avalonia AXAML files with minimal code-behind. All logic belongs in the ViewModel. Use compiled bindings (`{Binding ...}` with `x:DataType`).

### AXAML Guidelines

- Always set `x:DataType` on the root element of a View for compiled bindings.
- Keep code-behind files as thin as possible. Interaction logic belongs in the ViewModel via commands.
- Use Avalonia resource dictionaries for shared styles rather than inline styles.

---

## Adding a New AI Provider

To add a new AI provider (e.g., Google Gemini), follow these steps:

### 1. Implement `IAiProvider`

Create a new file in `Services/AI/`, for example `GeminiProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace VoxMemo.Services.AI;

public class GeminiProvider : IAiProvider
{
    public string ProviderName => "Gemini";

    public async Task<List<AiModel>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        // Return available models from the provider
    }

    public async Task<string> SummarizeAsync(
        string transcript, string promptType, string language,
        string model, CancellationToken ct = default)
    {
        // Send the transcript to the API and return the summary
    }

    public async IAsyncEnumerable<string> SummarizeStreamAsync(
        string transcript, string promptType, string language,
        string model, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Stream the summary token by token using yield return
    }
}
```

### 2. Register in Dependency Injection

Add the provider to the DI container in `App.axaml.cs` alongside the existing providers.

### 3. Expose in Settings

Update `SettingsViewModel` to include the new provider in the list of available AI providers so users can configure their API key or endpoint.

### 4. Add Localization Strings

Add any new UI strings to both `Resources/i18n/Strings.resx` (English) and `Resources/i18n/Strings.vi.resx` (Vietnamese).

---

## Adding a New Transcription Engine

To add a new transcription engine (e.g., a cloud-based service), follow these steps:

### 1. Implement `ITranscriptionService`

Create a new file in `Services/Transcription/`, for example `CloudTranscriptionService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace VoxMemo.Services.Transcription;

public class CloudTranscriptionService : ITranscriptionService
{
    public string EngineName => "cloud-stt";

    public event EventHandler<int>? ProgressChanged;

    public async Task<TranscriptionResult> TranscribeAsync(
        string audioPath, string language = "en",
        string? modelName = null, CancellationToken ct = default)
    {
        // Upload audio and return transcription results
        // Raise ProgressChanged to update the UI progress bar
    }

    public async Task<List<string>> GetAvailableModelsAsync()
    {
        // Return list of model identifiers
    }

    public async Task DownloadModelAsync(
        string modelName, IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        // No-op for cloud services, or download a local cache/config
    }
}
```

### 2. Register in DI

Register the new service in `App.axaml.cs`. You may register it as a keyed or named service if supporting multiple engines simultaneously.

### 3. Update the UI

Ensure `SettingsViewModel` and `RecordingViewModel` expose the new engine as a selectable option. The user should be able to choose between local Whisper and the new cloud engine.

---

## Adding a New View

To add a new page or panel to the application:

### 1. Create the ViewModel

Add a new file in `ViewModels/`, inheriting from `ViewModelBase`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace VoxMemo.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Analytics";

    // Add observable properties and relay commands here
}
```

### 2. Create the View

Add a new `.axaml` file in `Views/`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:VoxMemo.ViewModels"
             x:Class="VoxMemo.Views.AnalyticsView"
             x:DataType="vm:AnalyticsViewModel">
    <!-- Your UI here -->
</UserControl>
```

And its code-behind:

```csharp
using Avalonia.Controls;

namespace VoxMemo.Views;

public partial class AnalyticsView : UserControl
{
    public AnalyticsView()
    {
        InitializeComponent();
    }
}
```

### 3. Register in ViewLocator

The `ViewLocator.cs` file resolves ViewModel types to their corresponding View types by naming convention. Ensure your ViewModel and View follow the naming pattern (`XxxViewModel` maps to `XxxView`). If you use a non-standard name, update the `ViewLocator` accordingly.

### 4. Add Navigation

Update `MainWindowViewModel` to include a property or command that navigates to the new view. Add a corresponding navigation button in `MainWindow.axaml`.

### 5. Localize

Add all user-facing strings to both `.resx` files under `Resources/i18n/`.

---

## Pull Request Guidelines

1. **Branch from main.** Create a feature branch with a descriptive name: `feature/gemini-provider`, `fix/recording-crash`, `docs/user-guide`.

2. **Keep PRs focused.** Each pull request should address a single concern. Do not mix unrelated changes.

3. **Write a clear description.** Explain what the PR does, why the change is needed, and how it was tested.

4. **Follow the code style.** Ensure your code matches the conventions described above. Run `dotnet format` before submitting.

5. **Update localization files.** If you add or modify UI strings, update both `Strings.resx` and `Strings.vi.resx`.

6. **No warnings.** The build should complete with zero warnings. Address any nullable reference type warnings.

7. **Test manually.** At a minimum, verify that the application launches, your feature works, and existing functionality is not broken.

8. **Keep commits clean.** Use clear, descriptive commit messages. Squash fixup commits before requesting review.

---

## Testing Guidelines

### Manual Testing

Until an automated test suite is established, all changes must be manually verified:

- Launch the application and confirm it starts without errors.
- Test the specific feature you changed end-to-end.
- Verify that recording, transcription, and summarization still work after your changes.
- Test with both English and Vietnamese UI languages.
- Confirm the system tray icon and menu function correctly.
- Test window close/minimize behavior (should minimize to tray).

### Writing Automated Tests

When adding automated tests:

- Place test projects in a `tests/` directory at the solution root.
- Name test projects `VoxMemo.Tests` or `VoxMemo.IntegrationTests`.
- Use xUnit as the test framework.
- Mock external dependencies (AI providers, audio hardware, file system) using interfaces.
- Test ViewModels independently from Views. ViewModels are designed to be testable without a UI.
- Aim for meaningful coverage of business logic rather than targeting a coverage percentage.

### What to Test

- **ViewModels:** Command execution, property changes, state transitions.
- **Services:** AI provider response parsing, transcription result mapping, database operations.
- **Models:** Validation logic, default values, entity relationships.
- **Edge cases:** Empty transcripts, network failures, missing audio files, unsupported languages.
