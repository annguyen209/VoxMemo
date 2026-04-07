# VoxMemo User Guide

VoxMemo is a desktop application for recording meetings, transcribing audio locally with Whisper, and generating AI-powered summaries. This guide covers installation, configuration, and day-to-day usage.

## Table of Contents

- [First Launch Setup](#first-launch-setup)
- [Downloading a Whisper Model](#downloading-a-whisper-model)
- [Setting Up Ollama](#setting-up-ollama)
- [Recording a Meeting](#recording-a-meeting)
- [Live Captions](#live-captions)
- [Viewing and Managing Meetings](#viewing-and-managing-meetings)
- [Transcribing a Recording](#transcribing-a-recording)
- [Summarizing with AI](#summarizing-with-ai)
- [Configuring AI Providers](#configuring-ai-providers)
- [Changing Storage Location](#changing-storage-location)
- [System Tray Behavior](#system-tray-behavior)
- [Keyboard Shortcuts](#keyboard-shortcuts)
- [Troubleshooting](#troubleshooting)

---

## First Launch Setup

1. Launch VoxMemo. The application opens to the Recording view.
2. Navigate to **Settings** to configure your preferences before your first recording:
   - Choose a storage path for audio files (or keep the default).
   - Download a Whisper model for transcription.
   - Configure at least one AI provider if you want meeting summaries.
   - Select your preferred UI language (English or Vietnamese).
3. Return to the Recording view. You are ready to record.

---

## Downloading a Whisper Model

VoxMemo uses Whisper.net for local, offline transcription. You must download at least one model before transcribing.

1. Go to **Settings**.
2. In the transcription section, you will see a list of available Whisper models (e.g., `tiny`, `base`, `small`, `medium`, `large`).
3. Select the model you want and click the download button.
4. A progress indicator will show the download status. Model files can be several hundred megabytes for larger models.
5. Once downloaded, the model is available for all future transcriptions.

**Model size guidance:**

| Model   | Size     | Speed    | Accuracy   |
|---------|----------|----------|------------|
| tiny    | ~75 MB   | Fastest  | Lower      |
| base    | ~140 MB  | Fast     | Moderate   |
| small   | ~460 MB  | Moderate | Good       |
| medium  | ~1.5 GB  | Slow     | High       |
| large   | ~3 GB    | Slowest  | Highest    |

For most meetings, the `small` or `base` model provides a good balance between speed and accuracy. Use `medium` or `large` if you need higher accuracy and have sufficient hardware.

---

## Setting Up Ollama

Ollama allows you to run AI models locally for free, private summarization.

1. Download and install Ollama from [https://ollama.com](https://ollama.com).
2. Open a terminal and pull a model:
   ```
   ollama pull llama3.2
   ```
3. Verify Ollama is running. It serves on `http://localhost:11434` by default.
4. In VoxMemo **Settings**, ensure the Ollama provider is enabled and the URL is set to `http://localhost:11434`.
5. Click the refresh button next to the model list to load your available Ollama models.

You can pull multiple models and switch between them in VoxMemo at any time.

---

## Recording a Meeting

### Choosing an Audio Source

VoxMemo can capture audio from two sources:

- **Microphone** -- Records your voice and nearby speakers through your default input device.
- **System Audio** -- Captures all audio playing on your computer via WASAPI loopback (e.g., meeting participants in a video call).

You can record from both sources simultaneously for complete meeting capture.

### Starting a Recording

1. Go to the **Recording** view.
2. Select your desired audio source(s).
3. Click the **Start** button to begin recording.
4. The elapsed time counter will begin running.

### Pausing and Resuming

- Click the **Pause** button to temporarily halt recording. Audio is not captured while paused.
- Click **Resume** to continue recording. The audio will be seamless in the final file.

### Stopping a Recording

- Click the **Stop** button to end the recording.
- The meeting will be saved with its audio file, duration, and timestamp.
- An auto-generated title will be assigned to the meeting.
- The recording appears in the Meetings list for later transcription and summarization.

---

## Live Captions

When live captions are enabled, VoxMemo transcribes audio in real time as you record.

- Captions appear at the bottom of the Recording view during an active session.
- This feature requires a downloaded Whisper model.
- Live captions are approximate and may differ slightly from a full post-recording transcription.
- They are useful for confirming that audio is being captured correctly and for following along during a meeting.

---

## Viewing and Managing Meetings

Navigate to the **Meetings** view to see all recorded meetings.

### Meeting List

- Meetings are displayed with their title, date, and duration.
- Select a meeting to view its details.

### Meeting Details

Each meeting has tabbed content:

- **Transcript** -- The full transcription text with timestamped segments. Each segment shows its start time, end time, text, and confidence score.
- **Summary** -- AI-generated summaries. You can generate multiple summaries with different providers or prompt types.

### Deleting a Meeting

Select a meeting and use the delete option to remove it. This deletes the meeting record, its transcripts, summaries, and optionally the audio file from disk.

---

## Transcribing a Recording

After a recording is saved:

1. Go to the **Meetings** view and select the meeting.
2. Click the **Transcribe** button.
3. Choose a Whisper model and language (English or Vietnamese).
4. Transcription begins. A progress indicator shows completion percentage.
5. When finished, the transcript appears in the Transcript tab with timestamped segments.

Transcription is performed entirely on your local machine. No audio is sent to any external service.

---

## Summarizing with AI

After a meeting has been transcribed:

1. Select the meeting in the **Meetings** view.
2. Click the **Summarize** button.
3. Choose an AI provider and model from the dropdowns.
4. Select a prompt type (e.g., meeting summary).
5. Click **Generate**. The summary streams into the Summary tab as it is produced.

You can generate multiple summaries for the same meeting using different providers, models, or prompt types. All summaries are saved and accessible from the Summary tab.

---

## Configuring AI Providers

VoxMemo supports three AI providers. Configure them in **Settings**.

### Ollama (Local)

- **URL**: The Ollama server address. Default is `http://localhost:11434`.
- **Model**: Select from models you have pulled locally.
- No API key required. All processing stays on your machine.

### OpenAI

- **API Key**: Enter your OpenAI API key. Obtain one from [https://platform.openai.com/api-keys](https://platform.openai.com/api-keys).
- **Model**: Select from available models (e.g., `gpt-4o`, `gpt-4o-mini`).
- Requires an internet connection. Usage is billed by OpenAI according to their pricing.

### Anthropic

- **API Key**: Enter your Anthropic API key. Obtain one from [https://console.anthropic.com/](https://console.anthropic.com/).
- **Model**: Select from available models (e.g., `claude-sonnet-4-20250514`).
- Requires an internet connection. Usage is billed by Anthropic according to their pricing.

You can configure multiple providers and switch between them per summarization request.

---

## Changing Storage Location

By default, VoxMemo stores audio recordings in a folder within your user profile directory.

To change the storage location:

1. Go to **Settings**.
2. Find the **Storage Path** setting.
3. Browse to or enter the path to your desired folder.
4. The change takes effect immediately for new recordings. Existing audio files are not moved automatically.

Make sure the chosen location has sufficient disk space. Audio recordings can be several hundred megabytes per hour depending on quality settings.

---

## System Tray Behavior

VoxMemo runs in the system tray to stay out of your way during meetings.

- **Closing the window** does not exit the application. It minimizes to the system tray. This is especially important during recording -- closing the window will not stop an active recording.
- **The tray icon** remains visible in the system notification area.
- **Right-click the tray icon** to access the context menu with options to show the window or exit the application.
- To fully exit VoxMemo, use the **Exit** option in the tray menu.

---

## Keyboard Shortcuts

VoxMemo currently relies on button-based interaction for its primary functions. Check the application menus and tooltips for any available keyboard shortcuts. Future releases may introduce global hotkeys for starting and stopping recordings.

---

## Troubleshooting

### Empty or Silent Recording

**Symptoms:** The recording completes but the audio file is silent or very quiet.

**Solutions:**
- Verify that the correct audio source is selected (microphone vs. system audio).
- Check that your microphone is not muted in Windows sound settings.
- For system audio capture, ensure that audio is actually playing on the system during recording (e.g., a meeting call is active).
- Check the Windows volume mixer to confirm VoxMemo has access to the audio device.
- Try a different microphone or audio device.

### Transcription Fails or Produces No Text

**Symptoms:** Transcription errors out or returns an empty result.

**Solutions:**
- Ensure you have downloaded a Whisper model in Settings. Transcription cannot proceed without a model.
- Verify that the audio file exists at the expected storage path.
- Try a larger Whisper model. The `tiny` model may struggle with noisy audio or accented speech.
- Check that the correct language is selected. Using the wrong language setting will produce poor results.
- If the application crashes during transcription, try the `base` or `small` model instead of `medium` or `large`, which require more memory.

### Ollama Not Connected

**Symptoms:** The model list is empty, or summarization fails with a connection error.

**Solutions:**
- Confirm that Ollama is installed and running. Open a terminal and run `ollama list` to verify.
- Check that the Ollama URL in Settings matches the actual server address (default: `http://localhost:11434`).
- Ensure you have pulled at least one model with `ollama pull <model-name>`.
- If Ollama is running on a different machine, verify that the host and port are correct and that the firewall allows the connection.
- Restart the Ollama service and try refreshing the model list in VoxMemo.

### OpenAI or Anthropic API Errors

**Symptoms:** Summarization fails with authentication or rate limit errors.

**Solutions:**
- Double-check that your API key is entered correctly in Settings. Keys are long strings and easy to truncate.
- Verify that your API account has available credits or an active billing plan.
- If you receive rate limit errors, wait a moment and try again.
- Check your internet connection. Cloud AI providers require network access.

### Application Does Not Start

**Symptoms:** VoxMemo crashes or shows an error on launch.

**Solutions:**
- Ensure that .NET 10 runtime is installed on your system.
- Check the console output or log files for specific error messages.
- Delete the SQLite database file to reset settings if the database has become corrupted. The database will be recreated on next launch.
- Run the application from a terminal (`dotnet VoxMemo.dll`) to see detailed error output.

### High Memory Usage During Transcription

**Symptoms:** The system becomes slow or unresponsive during transcription.

**Solutions:**
- Use a smaller Whisper model. The `large` model requires several gigabytes of RAM.
- Close other memory-intensive applications during transcription.
- For very long recordings, consider splitting the audio into shorter segments before transcribing.
