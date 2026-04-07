# VoxMemo -- Architecture Document

**Version:** 1.0
**Last Updated:** 2026-04-07
**Status:** Draft

---

## 1. System Overview

```
+------------------------------------------------------------------+
|                        VoxMemo Desktop App                       |
|                         (Avalonia UI / .NET 10)                  |
+------------------------------------------------------------------+
|  UI Layer (MVVM)                                                 |
|  +------------------+  +------------------+  +-----------------+ |
|  | Recording View   |  | Meeting List     |  | Settings View   | |
|  | (Live Captions)  |  | (Search/Browse)  |  | (Config/Keys)   | |
|  +--------+---------+  +--------+---------+  +--------+--------+ |
|           |                      |                     |         |
+-----------+----------------------+---------------------+---------+
|  ViewModel Layer                                                 |
|  +------------------+  +------------------+  +-----------------+ |
|  | RecordingVM      |  | MeetingListVM    |  | SettingsVM      | |
|  +--------+---------+  +--------+---------+  +--------+--------+ |
|           |                      |                     |         |
+-----------+----------------------+---------------------+---------+
|  Service Layer                                                   |
|  +----------------+  +-------------------+  +-----------------+  |
|  | IAudioRecorder |  | ITranscription-   |  | IAiProvider     |  |
|  | (WASAPI)       |  |  Service (Whisper)|  | (Ollama/OAI/AC) |  |
|  +-------+--------+  +--------+----------+  +--------+--------+  |
|          |                     |                      |          |
|  +-------+--------+  +--------+----------+  +--------+--------+  |
|  | IAudioConverter |  | IModelManager    |  | ISummary-       |  |
|  | (WAV/FFmpeg)    |  | (Download/Cache) |  |  Generator      |  |
|  +-------+--------+  +--------+----------+  +--------+--------+  |
|          |                     |                      |          |
+-----------+----------------------+---------------------+---------+
|  Data Layer                                                      |
|  +----------------------------------------------------------+   |
|  | SQLite (via EF Core)                                      |   |
|  | Tables: meetings, transcripts, transcript_segments,       |   |
|  |         summaries, app_settings                           |   |
|  +----------------------------------------------------------+   |
+------------------------------------------------------------------+
```

---

## 2. Tech Stack Decisions and Rationale

| Technology | Role | Rationale |
|---|---|---|
| **.NET 10** | Application runtime | LTS release with strong cross-platform support, mature ecosystem, and excellent performance. Native AOT compilation is available if startup time becomes critical. |
| **Avalonia UI** | Desktop UI framework | True cross-platform UI (Windows, macOS, Linux) from a single codebase. Avoids WPF's Windows-only limitation. XAML-based, so familiar to .NET desktop developers. Active community and commercial backing. |
| **Whisper.net** | Speech-to-text | Managed .NET wrapper around whisper.cpp. Runs entirely on-device with no network dependency. Supports multiple model sizes and languages. Avoids the need to shell out to a Python process. |
| **NAudio (WASAPI)** | Audio capture (Windows) | Industry-standard .NET audio library. WASAPI loopback capture enables recording system audio without a virtual audio cable. Low-latency, well-documented. |
| **SQLite (EF Core)** | Local data storage | Zero-configuration embedded database. Single file for easy backup and portability. EF Core provides migrations, LINQ queries, and a familiar data access pattern. |
| **Ollama** | Local AI inference | Enables fully offline summarization by running open-weight LLMs locally. No API key required. Users with capable hardware get complete privacy. |
| **OpenAI / Anthropic APIs** | Cloud AI inference | Higher-quality summarization for users willing to use cloud services. Both are widely adopted and provide reliable APIs. Supporting multiple providers avoids vendor lock-in. |
| **CommunityToolkit.Mvvm** | MVVM framework | Lightweight source-generator-based MVVM toolkit. Reduces boilerplate for commands, observable properties, and messaging. |

---

## 3. Project Structure

```
VoxMemo/
├── docs/
│   ├── PRD.md                          # Product requirements document
│   └── ARCHITECTURE.md                 # This file
├── src/
│   ├── VoxMemo/                        # Main executable (Avalonia app)
│   │   ├── App.axaml                   # Application entry, theme, resources
│   │   ├── App.axaml.cs
│   │   ├── Program.cs                  # Entry point, host builder
│   │   ├── ViewModels/
│   │   │   ├── MainWindowViewModel.cs  # Shell/navigation VM
│   │   │   ├── RecordingViewModel.cs   # Active recording state, live captions
│   │   │   ├── MeetingListViewModel.cs # Browse/search meetings
│   │   │   ├── MeetingDetailViewModel.cs # Single meeting transcript + summary
│   │   │   └── SettingsViewModel.cs    # Configuration VM
│   │   ├── Views/
│   │   │   ├── MainWindow.axaml        # Shell window with navigation
│   │   │   ├── RecordingView.axaml     # Recording controls, waveform, captions
│   │   │   ├── MeetingListView.axaml   # List/grid of past meetings
│   │   │   ├── MeetingDetailView.axaml # Transcript + summary display
│   │   │   └── SettingsView.axaml      # Settings form
│   │   ├── Converters/                 # XAML value converters
│   │   ├── Assets/                     # Icons, fonts, images
│   │   └── VoxMemo.csproj
│   │
│   ├── VoxMemo.Core/                   # Shared domain models and interfaces
│   │   ├── Models/
│   │   │   ├── Meeting.cs
│   │   │   ├── Transcript.cs
│   │   │   ├── TranscriptSegment.cs
│   │   │   ├── Summary.cs
│   │   │   └── AppSettings.cs
│   │   ├── Interfaces/
│   │   │   ├── IAudioRecorder.cs       # Start/stop/pause recording
│   │   │   ├── IAudioConverter.cs      # WAV normalization, format conversion
│   │   │   ├── ITranscriptionService.cs# Transcribe audio, stream segments
│   │   │   ├── IModelManager.cs        # Download and manage Whisper models
│   │   │   ├── IAiProvider.cs          # Send prompt, receive completion
│   │   │   ├── ISummaryGenerator.cs    # Orchestrate summarization pipeline
│   │   │   └── IMeetingRepository.cs   # CRUD operations for meetings
│   │   └── VoxMemo.Core.csproj
│   │
│   ├── VoxMemo.Services/              # Concrete service implementations
│   │   ├── Audio/
│   │   │   ├── WasapiAudioRecorder.cs  # Windows WASAPI mic + loopback
│   │   │   └── AudioConverter.cs       # WAV processing, channel mixing
│   │   ├── Transcription/
│   │   │   ├── WhisperTranscriptionService.cs  # Whisper.net integration
│   │   │   └── WhisperModelManager.cs          # Model download and caching
│   │   ├── Ai/
│   │   │   ├── OllamaProvider.cs       # Local Ollama HTTP API client
│   │   │   ├── OpenAiProvider.cs       # OpenAI chat completions client
│   │   │   ├── AnthropicProvider.cs    # Anthropic messages API client
│   │   │   └── SummaryGenerator.cs     # Prompt construction, AI orchestration
│   │   └── VoxMemo.Services.csproj
│   │
│   └── VoxMemo.Data/                  # Data access layer
│       ├── VoxMemoDbContext.cs          # EF Core DbContext
│       ├── Migrations/                 # EF Core migrations
│       ├── Repositories/
│       │   └── MeetingRepository.cs    # SQLite-backed meeting persistence
│       └── VoxMemo.Data.csproj
│
├── tests/
│   ├── VoxMemo.Core.Tests/
│   ├── VoxMemo.Services.Tests/
│   └── VoxMemo.Data.Tests/
│
├── VoxMemo.sln
└── .gitignore
```

---

## 4. Data Flow

The primary data flow from recording to final output proceeds through six stages:

```
[1] RECORD        [2] CONVERT       [3] TRANSCRIBE
 Mic ──┐                                 
       ├─► Raw ──► WAV (16kHz ──► Whisper.net ──► Segments[]
 Sys ──┘   PCM     mono, 16-bit)         │
                                          │
                                    [4] STORE
                                          │
                                          ▼
                                     SQLite DB
                                     (meeting + transcript
                                      + segments)
                                          │
                                    [5] SUMMARIZE
                                          │
                                          ▼
                                     IAiProvider
                                     (Ollama / OpenAI / Anthropic)
                                          │
                                    [6] PRESENT
                                          │
                                          ▼
                                     UI (transcript + summary)
```

### Stage Details

1. **Record.** `IAudioRecorder` captures PCM audio from the microphone and system loopback simultaneously using WASAPI. The two streams are mixed into a single buffer and written to a temporary file.

2. **Convert.** `IAudioConverter` takes the raw capture and produces a WAV file normalized to 16 kHz, mono, 16-bit PCM -- the format Whisper expects. If the source is already in the correct format, this step is a pass-through.

3. **Transcribe.** `ITranscriptionService` feeds the WAV file to Whisper.net. The service yields `TranscriptSegment` objects (start time, end time, text) as they become available. During a live recording, segments are streamed to the UI for real-time captions.

4. **Store.** Once recording ends and transcription completes, the `Meeting`, `Transcript`, and `TranscriptSegment` records are persisted to SQLite via `IMeetingRepository`. The WAV file path is stored so audio can be replayed later.

5. **Summarize.** `ISummaryGenerator` constructs a prompt containing the full transcript text and sends it to the configured `IAiProvider`. The returned summary is stored in the `summaries` table and linked to the meeting. Auto-title generation uses a shorter prompt against the first few segments.

6. **Present.** The UI displays the meeting list, and on selection, shows the full transcript with timestamps and the generated summary.

---

## 5. Database Schema

All tables use integer primary keys with autoincrement. Timestamps are stored as UTC ISO 8601 strings.

```sql
CREATE TABLE meetings (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    title           TEXT        NOT NULL DEFAULT 'Untitled Meeting',
    recorded_at     TEXT        NOT NULL,  -- ISO 8601 UTC
    duration_secs   INTEGER     NOT NULL DEFAULT 0,
    audio_file_path TEXT        NULL,      -- path to WAV on disk
    language        TEXT        NOT NULL DEFAULT 'en',
    status          TEXT        NOT NULL DEFAULT 'recording',
                                           -- recording | transcribing | summarizing | completed | failed
    created_at      TEXT        NOT NULL,
    updated_at      TEXT        NOT NULL
);

CREATE TABLE transcripts (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    meeting_id      INTEGER     NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
    full_text       TEXT        NOT NULL DEFAULT '',
    whisper_model   TEXT        NOT NULL DEFAULT 'base',
    language        TEXT        NOT NULL DEFAULT 'en',
    created_at      TEXT        NOT NULL
);

CREATE TABLE transcript_segments (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    transcript_id   INTEGER     NOT NULL REFERENCES transcripts(id) ON DELETE CASCADE,
    segment_index   INTEGER     NOT NULL,
    start_ms        INTEGER     NOT NULL,  -- milliseconds from recording start
    end_ms          INTEGER     NOT NULL,
    text            TEXT        NOT NULL,
    confidence      REAL        NULL       -- 0.0 to 1.0, if available
);

CREATE TABLE summaries (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    meeting_id      INTEGER     NOT NULL REFERENCES meetings(id) ON DELETE CASCADE,
    provider        TEXT        NOT NULL,  -- ollama | openai | anthropic
    model           TEXT        NOT NULL,  -- e.g., llama3, gpt-4o, claude-sonnet
    prompt_tokens   INTEGER     NULL,
    completion_tokens INTEGER   NULL,
    content         TEXT        NOT NULL,
    created_at      TEXT        NOT NULL
);

CREATE TABLE app_settings (
    key             TEXT PRIMARY KEY,
    value           TEXT        NOT NULL,
    updated_at      TEXT        NOT NULL
);

-- Indexes
CREATE INDEX ix_transcripts_meeting   ON transcripts(meeting_id);
CREATE INDEX ix_segments_transcript   ON transcript_segments(transcript_id);
CREATE INDEX ix_summaries_meeting     ON summaries(meeting_id);
CREATE INDEX ix_meetings_recorded_at  ON meetings(recorded_at DESC);
```

### Key Settings (app_settings)

| Key | Example Value | Purpose |
|---|---|---|
| `ai.provider` | `ollama` | Active AI provider |
| `ai.ollama.endpoint` | `http://localhost:11434` | Ollama server URL |
| `ai.ollama.model` | `llama3` | Ollama model name |
| `ai.openai.api_key` | `sk-...` | OpenAI API key |
| `ai.openai.model` | `gpt-4o` | OpenAI model name |
| `ai.anthropic.api_key` | `sk-ant-...` | Anthropic API key |
| `ai.anthropic.model` | `claude-sonnet-4-20250514` | Anthropic model name |
| `whisper.model` | `base` | Whisper model size |
| `whisper.language` | `en` | Transcription language |
| `audio.input_device` | `(device ID)` | Selected microphone |
| `audio.record_system` | `true` | Capture system audio |
| `ui.theme` | `dark` | UI theme preference |

---

## 6. Service Layer Design

The service layer is defined by interfaces in `VoxMemo.Core` and implemented in `VoxMemo.Services`. All services are registered in the DI container at startup.

### IAudioRecorder

```csharp
public interface IAudioRecorder
{
    event EventHandler<AudioDataEventArgs> DataAvailable;
    event EventHandler<RecordingStoppedEventArgs> RecordingStopped;

    Task StartAsync(AudioRecordingOptions options, CancellationToken ct);
    Task PauseAsync();
    Task ResumeAsync();
    Task<string> StopAsync();  // Returns path to WAV file

    bool IsRecording { get; }
    bool IsPaused { get; }
    TimeSpan Elapsed { get; }
}
```

The Windows implementation (`WasapiAudioRecorder`) opens two WASAPI capture sessions -- one for the selected microphone and one for loopback -- mixes them in real time, and writes to a WAV file.

### ITranscriptionService

```csharp
public interface ITranscriptionService
{
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        string wavFilePath,
        TranscriptionOptions options,
        CancellationToken ct);

    IAsyncEnumerable<TranscriptSegment> TranscribeStreamAsync(
        IAudioRecorder recorder,
        TranscriptionOptions options,
        CancellationToken ct);
}
```

`TranscribeAsync` processes a completed WAV file. `TranscribeStreamAsync` accepts a live recorder for real-time captioning, buffering audio in chunks and feeding each chunk to Whisper.

### IAiProvider

```csharp
public interface IAiProvider
{
    string ProviderName { get; }

    Task<AiResponse> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        AiRequestOptions options,
        CancellationToken ct);

    IAsyncEnumerable<string> StreamCompleteAsync(
        string systemPrompt,
        string userPrompt,
        AiRequestOptions options,
        CancellationToken ct);
}
```

Three implementations: `OllamaProvider`, `OpenAiProvider`, `AnthropicProvider`. Each wraps the respective HTTP API. `ISummaryGenerator` calls `IAiProvider` internally and is responsible for prompt construction (e.g., "Summarize the following meeting transcript...").

### IModelManager

```csharp
public interface IModelManager
{
    Task<string> EnsureModelAsync(string modelName, IProgress<double> progress, CancellationToken ct);
    IReadOnlyList<ModelInfo> GetAvailableModels();
    Task DeleteModelAsync(string modelName);
}
```

Downloads Whisper GGML model files from Hugging Face on first use and caches them in the application data directory.

---

## 7. Platform Considerations

| Platform | Audio Backend | Status |
|---|---|---|
| **Windows** | WASAPI via NAudio | Primary target. Microphone and loopback capture both supported natively. |
| **macOS** | CoreAudio (planned) | Loopback capture on macOS requires a virtual audio device or ScreenCaptureKit (macOS 13+). This will need a platform-specific `IAudioRecorder` implementation. |
| **Linux** | PulseAudio / PipeWire (planned) | Monitor sources in PulseAudio/PipeWire enable loopback capture. A Linux-specific recorder will be needed. |

The `IAudioRecorder` interface abstracts platform differences. At startup, the DI container registers the appropriate implementation based on `RuntimeInformation.IsOSPlatform()`.

Avalonia UI renders natively on all three platforms, so the UI layer requires no platform-specific code. System tray behavior differs slightly per OS and is handled by Avalonia's `TrayIcon` API.

---

## 8. Key Design Decisions

### Why Avalonia UI instead of WPF, MAUI, or Electron

- **WPF** is Windows-only, ruling out future macOS and Linux support.
- **.NET MAUI** targets mobile-first and has limited desktop polish; its desktop story on Linux is nonexistent.
- **Electron** would add hundreds of megabytes of overhead and require maintaining a Node.js layer alongside .NET.
- **Avalonia** provides a XAML-based, native-feeling desktop experience on all three platforms with a single .NET codebase and a small runtime footprint.

### Why local-first architecture

- Privacy is a core value proposition. Users record sensitive meetings and must trust that audio and transcripts are not sent to external servers without explicit consent.
- Offline capability ensures VoxMemo works in air-gapped environments, during travel, or with unreliable connectivity.
- Local storage (SQLite) eliminates server costs and keeps the product viable as a free or one-time-purchase tool.

### Why Whisper.net instead of cloud STT or a Python subprocess

- **Cloud STT** (Google, Azure, AWS) contradicts the local-first principle and adds ongoing cost.
- **Calling Python** (original Whisper) requires users to install Python and manage dependencies -- a poor UX for a desktop app.
- **Whisper.net** wraps whisper.cpp in managed .NET code, ships as a NuGet package, and runs on CPU out of the box. GPU acceleration via CUDA or CoreML can be added later without changing the interface.

### Why multiple AI providers behind a single interface

- Users have different preferences and constraints. Some want full privacy (Ollama), others want the highest quality (OpenAI, Anthropic).
- An interface-based design allows adding new providers (Google Gemini, Mistral, local llama.cpp) without modifying existing code.
- The `ISummaryGenerator` depends only on `IAiProvider`, keeping summarization logic provider-agnostic.

### Why SQLite instead of a document store or flat files

- SQLite is embedded, zero-configuration, and battle-tested. No database server to install.
- Relational schema enables efficient queries (e.g., search across transcripts, filter by date range).
- EF Core migrations provide a clear upgrade path as the schema evolves.
- The single database file is trivial to back up or move between machines.
