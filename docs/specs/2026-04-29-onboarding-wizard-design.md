# VoxMemo — First-Run Onboarding Wizard Design

**Date:** 2026-04-29
**Status:** Approved

---

## Overview

A first-run wizard shown on cold start when the user has never completed setup. Sidebar layout (B) with 4 screens: Welcome → AI Provider → Whisper Model → Audio Device. Entirely skippable via "Set up later" on the Welcome screen.

---

## Trigger

On app startup, `App.axaml.cs` reads `app_settings` for key `"onboarding_complete"`. If the key is missing or not `"true"`, the `OnboardingWindow` is shown modally before `MainWindow` is made visible.

Both "Finish" (completed all steps) and "Set up later" (skipped) write `onboarding_complete = "true"` to the DB so the wizard never appears again. Users can re-run setup from Settings in the future if needed.

---

## Window

**`Views/OnboardingWindow.axaml`** — a standalone `Window` (not a `Dialog`) shown before MainWindow, sized 640×420, not resizable, `WindowStartupLocation.CenterScreen`.

Layout: two-column. Left sidebar (160px, `#11111b`) shows the step list with numbered circles (upcoming / active / done states). Right panel fills remaining width and hosts the current step's content, swapped by `SelectedPageIndex`.

---

## ViewModel

**`ViewModels/OnboardingViewModel.cs`** — single VM for the entire wizard.

Properties:
- `CurrentStep` — int 0–3 (Welcome=0, AIProvider=1, WhisperModel=2, AudioDevice=3)
- `SelectedAiProvider` — string ("Ollama" / "OpenAI" / "Anthropic")
- `ApiKey` — string (shown/hidden when cloud provider selected)
- `OllamaStatus` — string (e.g. "✓ Detected at localhost:11434" or "Not running")
- `AvailableDevices` — `ObservableCollection<AudioDeviceItem>` (loaded from `PlatformServices.AudioRecorder`)
- `SelectedDevice` — `AudioDeviceItem?`
- `DownloadableModels` — list of `{ Name, SizeMb, IsRecommended }`
- `IsDownloading` — bool
- `DownloadProgress` — string
- `ModelDownloaded` — bool (enables Next button on Whisper step)

Commands:
- `NextCommand` — advances `CurrentStep`; on step 3 saves all settings and closes
- `BackCommand` — decrements `CurrentStep` (shown on step 3 only)
- `SkipCommand` — writes `onboarding_complete = "true"`, closes window
- `DownloadModelCommand(string modelName)` — downloads selected Whisper model, sets `ModelDownloaded = true`
- `CheckOllamaCommand` — pings Ollama endpoint, updates `OllamaStatus`

---

## Screens

### Welcome (step 0)
Centered content: VoxMemo logo text, tagline "Record. Transcribe. Summarize.", one-line note "Takes 2 minutes. Everything runs locally.", "Get Started" primary button, "Set up later" text link below it.

### AI Provider (step 1)
Three provider cards (Ollama / OpenAI / Anthropic). Selecting Ollama shows a live connection status line (auto-checked on step enter). Selecting OpenAI or Anthropic reveals a password `TextBox` for the API key. "Skip for now" link at bottom-left.

### Whisper Model (step 2)
Three model rows: `base` (recommended badge, 142 MB), `tiny` (75 MB), `small` (466 MB). Each row has a Download button. Clicking Download shows inline progress text ("Downloading base… 38 MB / 142 MB"). Next button is disabled until a model finishes downloading. "Skip — download later in Settings" link bypasses this step.

### Audio Device (step 3)
Lists microphone devices from `IAudioRecorder.GetInputDevices()` with "Auto (Default)" pre-selected at top. Back button on the left. "Finish →" on the right saves everything and closes.

---

## Data Saved on Finish

| Setting key | Source |
|---|---|
| `ai_provider` | `SelectedAiProvider` |
| `openai_api_key` / `anthropic_api_key` | `ApiKey` (encrypted via `SecureStorage`) |
| `whisper_model` | downloaded model name (if downloaded) |
| `audio.input_device` | `SelectedDevice.Id` |
| `onboarding_complete` | `"true"` |

---

## Files

### New
- `Views/OnboardingWindow.axaml` + `.axaml.cs`
- `ViewModels/OnboardingViewModel.cs`

### Modified
- `App.axaml.cs` — check `onboarding_complete` on startup; show `OnboardingWindow` before `MainWindow`

---

## Spec Notes

- The Whisper download runs on a background task; `IsDownloading` disables all navigation while active so the user can't advance mid-download.
- `SkipCommand` and the "Skip for now" link on each step both call the same logic: save whatever is already set and write `onboarding_complete = "true"`.
- If `OnboardingWindow` is closed via the window chrome (X button), treat it the same as "Set up later" — mark complete so it doesn't re-appear.
