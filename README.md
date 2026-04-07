# VoxMemo

A desktop meeting recorder with local transcription, speaker identification, and AI-powered summarization. Built with .NET 10 and Avalonia UI.

---

## Features

### Recording
- Record from **microphone** or **system audio** (WASAPI loopback)
- **Live captions** while recording (Whisper tiny model, auto-downloaded)
- **Pause/resume** recording
- **Global hotkey** (default Ctrl+Shift+R) to start/stop from anywhere
- **System tray** with full controls: start, stop, pause, audio source, device, language

### Transcription
- Local speech-to-text powered by **Whisper.net** (no cloud required)
- Supports 30+ languages including English, Vietnamese, Chinese, Japanese, Korean, French, German, Spanish
- Import and use **custom Whisper models** (any ggml-format .bin file)
- Auto-converts imported audio (MP3, M4A, OGG, FLAC, etc.) to Whisper-compatible WAV

### AI Processing
- **Smart Process** -- one-click pipeline: Transcribe -> Identify Speakers -> Summarize
- **Speaker identification** -- AI reformats transcript as dialog with speaker labels
- **Meeting summarization** -- generates structured summaries referencing speakers by name
- **Auto-title generation** -- AI creates descriptive meeting titles from content
- Supports **Ollama** (local), **OpenAI**, **Anthropic**, **LM Studio**, and any OpenAI-compatible API
- **Custom AI prompts** -- override summary and speaker identification instructions
- **Prompt templates** -- pre-built templates for action items, executive briefs, technical reviews, interviews, standups, brainstorms

### Meetings Management
- **Import audio** -- upload existing MP3, WAV, M4A, OGG, FLAC, WMA, AAC files
- **Audio player** with play, pause, stop, and seek slider
- **Editable transcripts** -- edit text before summarizing for better results
- **Copy and export** -- copy to clipboard or export transcript/summary/audio as files
- **Search** meetings by title, platform, or date
- **Delete** with confirmation dialog

### Job Queue
- Background **processing queue** -- all heavy work runs off the UI thread
- **Real-time progress** -- status, provider/model info, elapsed timer for each job
- **Cancel** active jobs or **remove** queued ones
- **Toast notifications** on job completion/failure
- Jobs process **sequentially** in guaranteed order (no concurrent crashes)

### Settings
- **AI provider** configuration (Ollama URL, OpenAI base URL + key, Anthropic key)
- **Dynamic model list** -- fetches available models from any provider
- **Whisper model management** -- download built-in models, import custom .bin files, open models folder
- **Automation** -- auto-transcribe and auto-summarize after recording
- **Configurable languages** -- add/remove from 30+ Whisper-supported languages
- **Global hotkey** -- customizable (Ctrl+Shift+R, Alt+F9, etc.)
- **Start with Windows** -- auto-launch via registry
- **Storage location** -- choose where recordings are saved
- **Smart Process preferences** -- skip dialog option with saved step selection

### Other
- **Dark theme** -- Catppuccin Mocha color scheme
- **System tray** -- minimize to tray, recording continues in background
- **Logging** -- Serilog file logging with daily rotation (7-day retention)
- **Status bar** -- shows processing status, job queue count, author credit

## Prerequisites

| Requirement | Windows | Linux | macOS |
|---|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Required | Required | Required |
| ffmpeg + ffprobe | Optional (import only) | **Required** | **Required** |
| PulseAudio (parecord) | -- | **Required** (recording) | -- |
| ffplay | -- | Recommended (playback) | -- |
| notify-send | -- | Recommended (notifications) | -- |
| [BlackHole](https://github.com/ExistentialAudio/BlackHole) | -- | -- | Required for system audio |
| [Ollama](https://ollama.com) | Optional | Optional | Optional |

### Platform install commands

**Linux (Debian/Ubuntu):**
```bash
sudo apt install ffmpeg pulseaudio-utils libnotify-bin
```

**Linux (Fedora):**
```bash
sudo dnf install ffmpeg pulseaudio-utils libnotify
```

**Linux (Arch):**
```bash
sudo pacman -S ffmpeg pulseaudio libnotify
```

**macOS:**
```bash
brew install ffmpeg
# For system audio capture, install BlackHole:
# https://github.com/ExistentialAudio/BlackHole
```

OpenAI, Anthropic, LM Studio, or any OpenAI-compatible API can be used instead of Ollama.

## Getting Started

### Clone and build

```bash
git clone https://github.com/annguyen209/VoxMemo.git
cd VoxMemo
dotnet restore
dotnet build
```

### Run

```bash
dotnet run
```

### Run tests

```bash
cd ../VoxMemoTests
dotnet test
```

### Build all platforms

```bash
./build.sh
```

Or build individually:

```bash
# Windows
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Linux
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true

# macOS (Intel)
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true

# macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true
```

## First-Time Setup

### 1. Download a Whisper model

Go to **Settings > Transcription** and click one of the download buttons:

| Model | Size | Speed | Accuracy |
|---|---|---|---|
| tiny | ~75 MB | Fastest | Basic (recommended for live captions) |
| base | ~150 MB | Fast | Good |
| small | ~500 MB | Medium | Better |
| medium | ~1.5 GB | Slow | High |
| large | ~3 GB | Slowest | Best |

Or import any custom ggml-format model via **Import Model File** or drop it in the models folder.

### 2. Set up an AI provider

**Ollama (local, free)**
```bash
ollama pull gemma2
```
Ensure Ollama is running at `http://localhost:11434`.

**LM Studio / LocalAI / vLLM**
Select "OpenAI" provider, set the Base URL (e.g. `http://localhost:1234/v1`), leave API key empty.

**OpenAI or Anthropic**
Enter your API key in Settings. Models are fetched dynamically.

### 3. Record or import

- Click **Start Recording** to capture a meeting
- Or go to **Meetings > + New Meeting** to import an existing audio file
- Click **Smart Process** to transcribe, identify speakers, and summarize in one step

## Project Structure

```
VoxMemo/
  Assets/                       App icons and resources
  Models/
    Meeting.cs                  Meeting, Transcript, Summary entities
    WhisperLanguages.cs         Supported language list
  Services/
    AI/
      IAiProvider.cs            AI provider interface
      OllamaProvider.cs         Ollama via direct HTTP
      OpenAiProvider.cs         OpenAI-compatible API
      AnthropicProvider.cs      Anthropic API
      AiProviderFactory.cs      Creates provider from settings
      PromptTemplates.cs        Prompt definitions (with custom override)
    Audio/
      IAudioRecorder.cs         Audio recorder interface
    Platform/
      PlatformServices.cs       Central factory (runtime OS detection)
      IAudioConverter.cs        Audio conversion interface
      IAudioPlaybackService.cs  Playback interface
      IGlobalHotkeyService.cs   Hotkey interface
      INotificationService.cs   Notification interface
      IStartupService.cs        Auto-start interface
      Windows/                  NAudio, Registry, P/Invoke implementations
      Linux/                    PulseAudio, ffmpeg, notify-send implementations
      MacOS/                    avfoundation, afplay, osascript implementations
      Stub/                     Safe fallbacks for unsupported features
    Database/
      AppDbContext.cs            EF Core SQLite context
    Transcription/
      WhisperTranscriptionService.cs  Whisper.net with global lock
  ViewModels/
    MainWindowViewModel.cs      Navigation, job queue, auto-processing
    RecordingViewModel.cs       Recording, live captions
    MeetingsViewModel.cs        Meeting list, playback, Smart Process
    SettingsViewModel.cs        All app settings
  Views/
    MainWindow.axaml            Shell, sidebar, status bar, queue panel
    RecordingView.axaml         Recording controls
    MeetingsView.axaml          Meeting detail, player, transcript/summary
    SettingsView.axaml          Settings form
  App.axaml                     Tray icon, theme
  App.axaml.cs                  Tray menu, global hotkey
  Program.cs                    Entry point, Serilog setup

VoxMemoTests/                   xUnit test project (65 tests)
```

## Tech Stack

| Component | Library | Notes |
|---|---|---|
| UI framework | Avalonia UI 11.3 | Cross-platform desktop |
| MVVM | CommunityToolkit.Mvvm 8.2 | Source generators |
| Audio (Windows) | NAudio 2.2 | WASAPI recording + WaveOut playback |
| Audio (Linux) | PulseAudio + ffmpeg | parecord + ffplay |
| Audio (macOS) | ffmpeg + afplay | avfoundation recording |
| Transcription | Whisper.net 1.8 | Local speech-to-text (all platforms) |
| AI (Ollama) | Direct HTTP | Replaced OllamaSharp for stability |
| Database | EF Core + SQLite 9.0 | Settings + meeting storage |
| Logging | Serilog 4.x | File + console sinks |
| Runtime | .NET 10 | |

## Supported Platforms

| Platform | Recording | System Audio | Playback | Hotkey | Auto-start |
|---|---|---|---|---|---|
| Windows 10/11 (x64) | NAudio WASAPI | Loopback | NAudio | Ctrl+Shift+R | Registry |
| Linux x64 | PulseAudio | Monitor source | ffplay | -- | XDG autostart |
| macOS x64/arm64 | ffmpeg | BlackHole required | afplay | -- | LaunchAgent |

## Logs

Logs are stored with daily rotation and 7-day retention:

| Platform | Path |
|---|---|
| Windows | `%AppData%\VoxMemo\logs\` |
| Linux | `~/.config/VoxMemo/logs/` |
| macOS | `~/Library/Application Support/VoxMemo/logs/` |

## License

This project is licensed under the [MIT License](LICENSE).

## Author

Anzdev4life
