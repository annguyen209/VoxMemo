# Changelog

All notable changes to VoxMemo will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-04-07

Initial release of VoxMemo, a desktop meeting recorder with local transcription and AI summarization.

### Added

- **Audio Recording** -- Record from microphone and system audio simultaneously using WASAPI. Supports pause and resume during a recording session.
- **Local Whisper Transcription** -- Transcribe recordings offline using Whisper.net. Supports English and Vietnamese language models with downloadable model files.
- **AI Summarization** -- Generate meeting summaries using configurable AI providers:
  - Ollama (local, self-hosted models)
  - OpenAI (GPT models via API)
  - Anthropic (Claude models via API)
- **Streaming Summarization** -- AI summaries stream token-by-token into the UI for immediate feedback.
- **Live Captions** -- Display real-time transcription captions while recording is in progress.
- **Auto-Generated Meeting Titles** -- Meetings receive automatically generated titles based on their content.
- **Meeting History** -- Browse past meetings with tabbed views for transcripts and summaries. Each meeting stores its audio path, duration, language, and timestamps.
- **Transcript Segments** -- Transcriptions include timestamped segments with confidence scores for fine-grained review.
- **System Tray Support** -- The application minimizes to the system tray instead of closing. Recording continues in the background when the window is hidden.
- **Settings Persistence** -- All application settings are stored in a local SQLite database via Entity Framework Core.
- **Configurable Storage Path** -- Users can choose where meeting audio files are saved on disk.
- **Dark Theme** -- Catppuccin Mocha dark theme applied across the entire application.
- **Bilingual UI** -- Full user interface localization for English and Vietnamese using .NET resource files.
- **MVVM Architecture** -- Clean separation of concerns using CommunityToolkit.Mvvm with compiled Avalonia bindings.
- **Dependency Injection** -- Services registered and resolved through Microsoft.Extensions.DependencyInjection.

[0.1.0]: https://github.com/your-org/VoxMemo/releases/tag/v0.1.0
