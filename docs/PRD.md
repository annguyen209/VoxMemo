# VoxMemo -- Product Requirements Document

**Version:** 1.0
**Last Updated:** 2026-04-07
**Status:** Draft

---

## 1. Problem Statement

Professionals, remote workers, and students participate in meetings, lectures, and calls daily. Capturing what was discussed accurately is difficult: manual note-taking is incomplete and distracting, cloud-based transcription services raise privacy concerns, and most existing tools lock users into subscriptions with data stored on third-party servers.

VoxMemo addresses these problems by providing a cross-platform desktop application that records audio (microphone and system output), transcribes it locally using Whisper, and generates AI-powered summaries -- all while keeping data on the user's machine by default.

---

## 2. Target Users

| Segment | Description |
|---|---|
| **Remote professionals** | Knowledge workers attending virtual meetings (Zoom, Teams, Google Meet) who need reliable meeting records without trusting a cloud service. |
| **In-person meeting attendees** | Professionals in conference rooms who want a one-click recorder that captures microphone audio and produces searchable transcripts. |
| **Students and researchers** | Individuals attending lectures or interviews who need verbatim transcripts and concise summaries for review. |
| **Freelancers and consultants** | Solo practitioners who must document client calls for accountability and follow-up. |

---

## 3. User Stories

| ID | Story | Priority |
|---|---|---|
| US-01 | As a user, I want to record system audio and microphone audio simultaneously so that both sides of a virtual meeting are captured. | P0 |
| US-02 | As a user, I want the recording to be transcribed locally using Whisper so that my audio never leaves my machine. | P0 |
| US-03 | As a user, I want to choose between English and Vietnamese transcription so that VoxMemo works for my primary languages. | P0 |
| US-04 | As a user, I want an AI-generated summary of each transcript so that I can review meetings quickly. | P0 |
| US-05 | As a user, I want to select my AI provider (Ollama, OpenAI, or Anthropic) so that I can use local or cloud models according to my preference. | P0 |
| US-06 | As a user, I want meetings stored in a local SQLite database so that I own my data and can back it up easily. | P0 |
| US-07 | As a user, I want live captions displayed during recording so that I can follow along in real time. | P1 |
| US-08 | As a user, I want meetings to receive auto-generated titles so that my meeting list is organized without manual effort. | P1 |
| US-09 | As a user, I want a system tray icon so that VoxMemo stays accessible without cluttering my taskbar. | P1 |
| US-10 | As a user, I want to search across all my transcripts so that I can find specific discussions from past meetings. | P1 |
| US-11 | As a user, I want to export a transcript and summary to a text or Markdown file so that I can share meeting notes with colleagues. | P1 |
| US-12 | As a user, I want to pause and resume a recording so that I can skip breaks or off-topic segments. | P1 |
| US-13 | As a user, I want to view timestamped transcript segments so that I can jump to a specific part of the conversation. | P2 |
| US-14 | As a user, I want to configure Whisper model size (tiny, base, small, medium, large) so that I can trade accuracy for speed based on my hardware. | P2 |
| US-15 | As a user, I want a dark and light theme so that the app matches my system appearance. | P2 |

---

## 4. Feature Requirements

### P0 -- Must Have (MVP)

| Feature | Description |
|---|---|
| **Audio recording** | Record from microphone and system audio (loopback) simultaneously. Produce WAV files. |
| **Local transcription** | Transcribe recorded audio using Whisper.net. Support English and Vietnamese language models. |
| **AI summarization** | Send transcript text to a configurable AI provider (Ollama for local, OpenAI or Anthropic for cloud) and return a structured summary. |
| **Meeting storage** | Persist meetings, transcripts, transcript segments, and summaries in a local SQLite database. |
| **Meeting list view** | Display all recorded meetings with title, date, duration, and summary preview. |
| **Meeting detail view** | Show full transcript and summary for a selected meeting. |
| **Settings screen** | Allow the user to configure AI provider, API keys, Whisper model, language, and audio devices. |

### P1 -- Should Have

| Feature | Description |
|---|---|
| **Live captions** | Stream Whisper output to the UI during recording for real-time subtitle display. |
| **Auto-titles** | Generate a short descriptive title from the first portion of the transcript using the configured AI provider. |
| **System tray** | Minimize to system tray; provide tray context menu for start/stop recording and open window. |
| **Transcript search** | Full-text search across all stored transcripts. |
| **Export** | Export meeting notes (transcript + summary) to `.md` or `.txt`. |
| **Pause/Resume** | Pause and resume an active recording session. |

### P2 -- Nice to Have

| Feature | Description |
|---|---|
| **Timestamped segments** | Display transcript segments with start/end timestamps; click to seek in a future audio playback feature. |
| **Whisper model selector** | Let the user choose model size and auto-download the selected model on first use. |
| **Theme support** | Light and dark themes that follow OS preference or can be set manually. |
| **Audio playback** | Play back recorded audio from within the meeting detail view. |
| **Speaker diarization** | Identify and label different speakers in the transcript. |
| **Keyboard shortcuts** | Global hotkey to start/stop recording without bringing the window to the foreground. |

---

## 5. Success Metrics

| Metric | Target |
|---|---|
| Transcription word error rate (WER) | Less than 15% for English using the "base" Whisper model on clear audio. |
| Recording start latency | Under 1 second from button click to audio capture beginning. |
| Transcription speed | At least 1x real-time on a modern CPU (i.e., a 60-minute meeting transcribed in 60 minutes or less using the "base" model). |
| Summary generation time | Under 30 seconds for a 30-minute meeting transcript via Ollama with a 7B model. |
| Application cold start | Under 3 seconds to a usable window on an SSD-equipped machine. |
| Data integrity | Zero data loss across 100 consecutive recording sessions in automated testing. |

---

## 6. Out of Scope / Future Considerations

The following items are explicitly excluded from the current version but may be considered for future releases:

- **Cloud sync and multi-device access.** VoxMemo is local-first. Sync may be explored later via a plugin or optional service.
- **Mobile applications.** The initial release targets desktop only (Windows, with macOS and Linux planned).
- **Video recording or screen capture.** VoxMemo is audio-only.
- **Real-time collaborative editing of transcripts.** Transcripts are single-user documents.
- **Built-in calendar integration.** Automatic scheduling or meeting detection from Outlook/Google Calendar is deferred.
- **End-to-end encryption at rest.** SQLite files are stored unencrypted. Users can apply OS-level disk encryption.
- **Plugin or extension system.** The architecture should be modular enough to support this later, but no public plugin API is in scope.

---

## 7. Technical Constraints

| Constraint | Detail |
|---|---|
| **Runtime** | .NET 10 (LTS). The application must run on the .NET 10 runtime without requiring additional frameworks. |
| **UI framework** | Avalonia UI for cross-platform desktop rendering. No WPF or WinForms dependencies. |
| **Audio capture** | Windows: WASAPI (via NAudio or a similar library) for both microphone and loopback capture. macOS/Linux audio backends are deferred. |
| **Transcription engine** | Whisper.net (managed wrapper around whisper.cpp). Models are downloaded on first use and stored locally. |
| **AI providers** | Must support at least three providers at launch: Ollama (local), OpenAI API, Anthropic API. Provider interface must be extensible. |
| **Database** | SQLite via Entity Framework Core or a lightweight ORM. Single-file database stored in the user's app data directory. |
| **Offline capability** | Recording and transcription must work fully offline. Summarization requires network access only when using a cloud AI provider; Ollama summarization works offline. |
| **Minimum hardware** | 8 GB RAM, 4-core CPU, 2 GB free disk space (excluding Whisper models). |
| **Installer** | Single-file or MSIX for Windows. Platform-specific packaging deferred for macOS/Linux. |
