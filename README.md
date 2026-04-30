# VoxMemo

> **Local-first meeting recorder** — record, transcribe, identify speakers, and summarize meetings entirely on your machine. No cloud. No subscriptions.

Built with .NET 10 and Avalonia UI. Runs on Windows, Linux, and macOS.

---

## Screenshots

<table>
  <tr>
    <td align="center">
      <img src="docs/screenshots/recording.png" alt="Recording view" width="380"/><br/>
      <sub><b>Recording</b> — Live captions while you record</sub>
    </td>
    <td align="center">
      <img src="docs/screenshots/meetings.png" alt="Meetings view" width="380"/><br/>
      <sub><b>Meetings</b> — Transcript, speakers, and AI summary</sub>
    </td>
  </tr>
  <tr>
    <td align="center">
      <img src="docs/screenshots/smart-process.png" alt="Smart Process pipeline" width="380"/><br/>
      <sub><b>Smart Process</b> — One click: transcribe → identify speakers → summarize</sub>
    </td>
    <td align="center">
      <img src="docs/screenshots/settings.png" alt="Settings view" width="380"/><br/>
      <sub><b>Settings</b> — Ollama, OpenAI, Anthropic, or any OpenAI-compatible API</sub>
    </td>
  </tr>
</table>

---

## Why VoxMemo?

| | VoxMemo | Otter.ai / Fireflies | Local Whisper scripts |
|---|---|---|---|
| **Privacy** | 100% local | Uploads to cloud | Local |
| **Cost** | Free | $10–$20/mo | Free |
| **Speaker ID** | ✅ AI-powered | ✅ | ❌ manual |
| **Custom AI models** | ✅ Ollama, LM Studio | ❌ | ❌ |
| **Works offline** | ✅ | ❌ | ✅ |
| **Desktop UI** | ✅ | Web/mobile | ❌ CLI |
| **Live captions** | ✅ | ✅ | ❌ |

---

## Features

### Recording
- **Mic + Speaker mix** — records both your voice and remote participants in one file (ideal for Zoom/Teams/Meet)
- Record from **microphone**, **system audio**, or **both simultaneously**
- **Live captions** while recording (Whisper tiny model, auto-downloaded) — new line per sentence, selectable and copyable
- **Pause/resume** recording
- **Global hotkey** (default Ctrl+Shift+R) to start/stop from anywhere
- **System tray** with full controls: start, stop, pause, audio source, device, language

### Transcription
- Local speech-to-text powered by **Whisper.net** — no cloud required, no data sent anywhere
- **Auto language detection** — Whisper automatically detects language, including mixed Vietnamese/English conversations
- Supports 30+ languages including English, Vietnamese, Chinese, Japanese, Korean, French, German, Spanish
- Import and use **custom Whisper models** (any ggml-format .bin file)
- Auto-converts imported audio (MP3, M4A, OGG, FLAC, etc.) to Whisper-compatible WAV

### AI Processing
- **Smart Process** — one-click pipeline: Transcribe → Identify Speakers → Summarize
- **Speaker identification** — AI reformats transcript as dialog with named speaker labels, shown in a dedicated Speakers tab (original transcript preserved)
- **Meeting summarization** — structured summaries referencing speakers by name
- **Auto-title generation** — AI creates descriptive meeting titles from content
- **Custom AI prompts** — override summary and speaker identification instructions
- **Prompt templates** — action items, executive briefs, technical reviews, interviews, standups, brainstorms
- Supports **Ollama** (local), **OpenAI**, **Anthropic**, **LM Studio**, and any OpenAI-compatible API

### Meetings Management
- **Import audio** — upload existing MP3, WAV, M4A, OGG, FLAC, WMA, AAC files
- **Import transcript** — paste text or load a .txt file to skip recording entirely
- **Audio player** with play, pause, stop, and seek slider — state preserved when switching tabs
- **Editable transcripts** — correct text before summarizing
- **Copy and export** — clipboard or file export for transcript/summary/audio
- **Search** meetings by title, platform, or date
- **Language badge** — per-meeting language tag, editable after recording

### Job Queue
- Background **processing queue** — all heavy work off the UI thread
- **Real-time progress** — status, provider/model info, elapsed timer per job
- **Cancel** active jobs or **remove** queued ones
- **Toast notifications** on completion/failure
- Jobs process **sequentially** in guaranteed order

### First-Run Setup
- **Onboarding wizard** on first launch — configure AI provider, download a Whisper model, and select audio device in one guided flow
- Re-run anytime from **Settings → Re-run Setup Wizard**

### Settings
- **AI provider** configuration with live model refresh
- **Whisper model management** — download built-in models, import custom .bin files
- **GPU acceleration** — Vulkan support for Intel Arc and AMD GPUs
- **AI timeout** — configurable 1–60 minute timeout per request
- **Automation** — auto-transcribe and auto-summarize after recording
- **Configurable languages** — add/remove from 30+ Whisper-supported languages
- **Global hotkey** — customizable (Windows only)
- **Start with Windows** — auto-launch via registry
- **Storage location** — choose where recordings are saved

---

## Download

👉 **[Latest release — v1.5.0](https://github.com/AnsCodeLab/VoxMemo/releases/latest)**

| Platform | Download |
|---|---|
| Windows (Installer) | `VoxMemo-v1.5.0-Setup.exe` |
| Windows (Portable) | `VoxMemo-v1.5.0-win-x64.zip` |
| Linux x64 | `VoxMemo-v1.5.0-linux-x64.tar.gz` |
| macOS Intel | `VoxMemo-v1.5.0-osx-x64.tar.gz` |
| macOS Apple Silicon | `VoxMemo-v1.5.0-osx-arm64.tar.gz` |

---

## Prerequisites

| Requirement | Windows | Linux | macOS |
|---|---|---|---|
| ffmpeg + ffprobe | Optional (import only) | **Required** | **Required** |
| PulseAudio (`parecord`) | — | **Required** (recording) | — |
| ffplay | — | Recommended (playback) | — |
| notify-send | — | Recommended (notifications) | — |
| [BlackHole](https://github.com/ExistentialAudio/BlackHole) | — | — | Required for system audio |
| [Ollama](https://ollama.com) | Optional | Optional | Optional |

### Install dependencies

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

---

## Getting Started

### 1. Download a Whisper model

Go to **Settings → Transcription** and download a model:

| Model | Size | Best for |
|---|---|---|
| tiny | ~75 MB | Live captions (auto-downloaded) |
| base | ~150 MB | Fast transcription |
| small | ~500 MB | Good balance |
| medium | ~1.5 GB | High accuracy |
| large-v3-turbo | ~1.5 GB | Best for Vietnamese / Asian languages |
| large-v3 | ~3 GB | Maximum accuracy |

### 2. Set up AI (optional, for summarization)

**Ollama (local, free — recommended):**
```bash
ollama pull gemma3        # fast, good quality
ollama pull qwen3:8b      # great for Vietnamese
```
Ollama runs at `http://localhost:11434` by default.

**LM Studio / LocalAI / vLLM:**
Select "OpenAI" provider, set Base URL (e.g. `http://localhost:1234/v1`), leave key empty.

**OpenAI / Anthropic:**
Enter your API key in Settings → AI Provider.

### 3. Record a meeting

1. Select **"Both (Mic + Speaker)"** as audio source for full meeting capture
2. Click **Start Recording** (or press Ctrl+Shift+R)
3. After the meeting, click **Stop**
4. Click **Smart Process** to transcribe, identify speakers, and summarize in one step

---

## Build from Source

```bash
git clone https://github.com/AnsCodeLab/VoxMemo.git
cd VoxMemo
dotnet restore
dotnet run
```

**Run tests:**
```bash
cd VoxMemoTests
dotnet test
```

**Build all platforms:**
```bash
./build.sh
```

---

## Tech Stack

| Component | Library |
|---|---|
| UI framework | Avalonia UI 11.3 |
| MVVM | CommunityToolkit.Mvvm 8.2 |
| Audio (Windows) | NAudio 2.2 — WASAPI + WaveOut |
| Audio (Linux) | PulseAudio + ffmpeg |
| Audio (macOS) | ffmpeg avfoundation + afplay |
| Transcription | Whisper.net 1.8 (local, all platforms) |
| GPU acceleration | Whisper.net.Runtime.Vulkan |
| Database | EF Core + SQLite 9.0 |
| Logging | Serilog 4.x |
| Runtime | .NET 10 |

## Supported Platforms

| Platform | Recording | System Audio | Playback | Hotkey | Auto-start |
|---|---|---|---|---|---|
| Windows 10/11 x64 | NAudio WASAPI | Loopback capture | NAudio | ✅ Ctrl+Shift+R | Registry |
| Linux x64 | PulseAudio | Monitor source | ffplay | — | XDG autostart |
| macOS x64/arm64 | ffmpeg | BlackHole required | afplay | — | LaunchAgent |

## Logs

| Platform | Path |
|---|---|
| Windows | `%AppData%\VoxMemo\logs\` |
| Linux | `~/.config/VoxMemo/logs/` |
| macOS | `~/Library/Application Support/VoxMemo/logs/` |

---

## License

[MIT License](LICENSE) — free to use, modify, and distribute.

## Author

**AnsCodeLab** — [github.com/AnsCodeLab](https://github.com/AnsCodeLab)
