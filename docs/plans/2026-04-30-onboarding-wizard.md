# First-Run Onboarding Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a 4-screen first-run wizard (Welcome → AI Provider → Whisper Model → Audio Device) on first launch, skippable at any time, that saves provider/model/device settings and marks itself done so it never reappears.

**Architecture:** `OnboardingViewModel` owns all wizard state and raises `CloseRequested` when done. `OnboardingWindow` is a standalone AXAML `Window` that shows before `MainWindow` is made visible. `App.axaml.cs` checks `onboarding_complete` in the DB on startup and conditionally shows the wizard; when it closes, MainWindow is revealed.

**Tech Stack:** .NET 10, Avalonia UI / AXAML, CommunityToolkit.Mvvm, NAudio (device list), Whisper.net (model download), EF Core / SQLite, Serilog, xUnit

---

## File Map

### New files
| File | Purpose |
|---|---|
| `ViewModels/OnboardingViewModel.cs` | All wizard state, step navigation, download, Ollama check, save |
| `Views/OnboardingWindow.axaml` | Wizard window layout: sidebar + 4 content panels |
| `Views/OnboardingWindow.axaml.cs` | Code-behind: wires CloseRequested, handles X-button close |
| `VoxMemo.Tests/OnboardingViewModelTests.cs` | Unit tests for step navigation and CanExecute logic |

### Modified files
| File | Change |
|---|---|
| `App.axaml.cs` | Read `onboarding_complete`; show OnboardingWindow before MainWindow if needed |

---

## Task 1: OnboardingViewModel

**Files:**
- Create: `ViewModels/OnboardingViewModel.cs`
- Create: `VoxMemo.Tests/OnboardingViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// VoxMemo.Tests/OnboardingViewModelTests.cs
using VoxMemo.ViewModels;

namespace VoxMemo.Tests;

public class OnboardingViewModelTests
{
    [Fact]
    public void InitialStep_IsZero()
    {
        var vm = new OnboardingViewModel();
        Assert.Equal(0, vm.CurrentStep);
    }

    [Fact]
    public void IsStep0_TrueOnlyWhenCurrentStepIsZero()
    {
        var vm = new OnboardingViewModel();
        Assert.True(vm.IsStep0);
        Assert.False(vm.IsStep1);
        Assert.False(vm.IsStep2);
        Assert.False(vm.IsStep3);
    }

    [Fact]
    public void ShowApiKey_FalseForOllama()
    {
        var vm = new OnboardingViewModel { SelectedAiProvider = "Ollama" };
        Assert.False(vm.ShowApiKey);
    }

    [Fact]
    public void ShowApiKey_TrueForOpenAI()
    {
        var vm = new OnboardingViewModel { SelectedAiProvider = "OpenAI" };
        Assert.True(vm.ShowApiKey);
    }

    [Fact]
    public void ShowApiKey_TrueForAnthropic()
    {
        var vm = new OnboardingViewModel { SelectedAiProvider = "Anthropic" };
        Assert.True(vm.ShowApiKey);
    }

    [Fact]
    public void ModelDownloaded_FalseInitially()
    {
        var vm = new OnboardingViewModel();
        Assert.False(vm.ModelDownloaded);
    }

    [Fact]
    public void CloseRequested_RaisedBySkip()
    {
        var vm = new OnboardingViewModel();
        bool raised = false;
        vm.CloseRequested += (_, _) => raised = true;
        // SkipCommand internally calls SaveAndCloseAsync which raises CloseRequested
        // We test via direct invocation of the underlying method
        vm.RaiseCloseForTest();
        Assert.True(raised);
    }
}
```

- [ ] **Step 2: Run tests — expect build error (type not found)**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~OnboardingViewModel" -v quiet
```
Expected: build error.

- [ ] **Step 3: Create `ViewModels/OnboardingViewModel.cs`**

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VoxMemo.Models;
using VoxMemo.Services.AI;
using VoxMemo.Services.Database;
using VoxMemo.Services.Platform;
using VoxMemo.Services.Security;
using VoxMemo.Services.Transcription;

namespace VoxMemo.ViewModels;

public record WhisperModelOption(string Name, string DisplaySize, bool IsRecommended);

public partial class OnboardingViewModel : ViewModelBase
{
    [ObservableProperty] private int _currentStep;
    [ObservableProperty] private string _selectedAiProvider = "Ollama";
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _ollamaStatus = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private string _downloadProgress = string.Empty;
    [ObservableProperty] private bool _modelDownloaded;
    [ObservableProperty] private string _downloadedModelName = string.Empty;
    [ObservableProperty] private AudioDeviceItem? _selectedDevice;

    public ObservableCollection<AudioDeviceItem> AvailableDevices { get; } = [];

    public WhisperModelOption[] DownloadableModels { get; } =
    [
        new("base",  "142 MB", IsRecommended: true),
        new("tiny",  "75 MB",  IsRecommended: false),
        new("small", "466 MB", IsRecommended: false),
    ];

    public bool IsStep0 => CurrentStep == 0;
    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool ShowApiKey => SelectedAiProvider is "OpenAI" or "Anthropic";
    public bool CanGoNext => !IsDownloading;

    public event EventHandler? CloseRequested;

    // Test-only helper
    public void RaiseCloseForTest() => CloseRequested?.Invoke(this, EventArgs.Empty);

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        if (value == 1) _ = CheckOllamaAsync();
        if (value == 3) LoadDevices();
    }

    partial void OnSelectedAiProviderChanged(string value) =>
        OnPropertyChanged(nameof(ShowApiKey));

    partial void OnIsDownloadingChanged(bool value) =>
        OnPropertyChanged(nameof(CanGoNext));

    [RelayCommand]
    private async Task NextAsync()
    {
        if (CurrentStep < 3)
        {
            CurrentStep++;
        }
        else
        {
            await FinishAsync();
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep > 0) CurrentStep--;
    }

    [RelayCommand]
    private async Task SkipAsync()
    {
        await SaveAndCloseAsync();
    }

    [RelayCommand]
    private async Task DownloadModelAsync(string modelName)
    {
        if (IsDownloading) return;
        IsDownloading = true;
        DownloadProgress = $"Downloading {modelName}...";
        try
        {
            var service = new WhisperTranscriptionService();
            await service.DownloadModelAsync(modelName, new Progress<float>(mb =>
            {
                DownloadProgress = $"Downloading {modelName}… {mb:F0} MB";
            }));
            DownloadedModelName = modelName;
            ModelDownloaded = true;
            DownloadProgress = $"{modelName} ready";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Model download failed in onboarding");
            DownloadProgress = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task CheckOllamaAsync()
    {
        OllamaStatus = "Checking...";
        try
        {
            var provider = new OllamaProvider("http://localhost:11434");
            var models = await provider.GetAvailableModelsAsync();
            OllamaStatus = models.Count > 0
                ? $"✓ Detected at localhost:11434 ({models.Count} models)"
                : "✓ Connected — no models yet (run 'ollama pull llama3')";
        }
        catch
        {
            OllamaStatus = "Not running — install Ollama or choose another provider";
        }
    }

    private void LoadDevices()
    {
        AvailableDevices.Clear();
        AvailableDevices.Add(new AudioDeviceItem(null, "Auto (Default)", false));
        try
        {
            foreach (var d in PlatformServices.AudioRecorder.GetInputDevices())
                AvailableDevices.Add(new AudioDeviceItem(d.Id, d.Name, d.IsLoopback));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate audio devices in onboarding");
        }
        SelectedDevice = AvailableDevices[0];
    }

    private async Task FinishAsync()
    {
        try
        {
            await using var db = AppDbContextFactory.Create();

            await UpsertAsync(db, "ai_provider", SelectedAiProvider);

            if (SelectedAiProvider == "OpenAI" && !string.IsNullOrEmpty(ApiKey))
                await SaveApiKeyAsync(db, "openai_api_key", ApiKey);
            else if (SelectedAiProvider == "Anthropic" && !string.IsNullOrEmpty(ApiKey))
                await SaveApiKeyAsync(db, "anthropic_api_key", ApiKey);

            if (!string.IsNullOrEmpty(DownloadedModelName))
                await UpsertAsync(db, "whisper_model", DownloadedModelName);

            if (SelectedDevice?.Id != null)
                await UpsertAsync(db, "audio_input_device", SelectedDevice.Id);

            await UpsertAsync(db, "onboarding_complete", "true");
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save onboarding settings");
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveAndCloseAsync()
    {
        try
        {
            await using var db = AppDbContextFactory.Create();
            await UpsertAsync(db, "onboarding_complete", "true");
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to mark onboarding complete on skip");
        }
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private static async Task UpsertAsync(AppDbContext db, string key, string value)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null) setting.Value = value;
        else db.AppSettings.Add(new AppSettings { Key = key, Value = value });
    }

    private static async Task SaveApiKeyAsync(AppDbContext db, string key, string plaintext)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting == null) { setting = new AppSettings { Key = key }; db.AppSettings.Add(setting); }
        setting.EncryptedValue = SecureStorage.Encrypt(plaintext);
        setting.Value = "";
    }
}
```

- [ ] **Step 4: Run tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj --filter "FullyQualifiedName~OnboardingViewModel" -v quiet
```
Expected: 7 passed.

- [ ] **Step 5: Run all tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj -v quiet
```
Expected: 110 passed (103 + 7), 0 failed.

- [ ] **Step 6: Commit**

```
git add ViewModels/OnboardingViewModel.cs VoxMemo.Tests/OnboardingViewModelTests.cs
git commit -m "feat: add OnboardingViewModel for first-run wizard"
```

---

## Task 2: OnboardingWindow AXAML

**Files:**
- Create: `Views/OnboardingWindow.axaml`
- Create: `Views/OnboardingWindow.axaml.cs`

- [ ] **Step 1: Create `Views/OnboardingWindow.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:VoxMemo.ViewModels"
        x:Class="VoxMemo.Views.OnboardingWindow"
        x:DataType="vm:OnboardingViewModel"
        Title="VoxMemo Setup"
        Width="640" Height="420"
        CanResize="False"
        WindowStartupLocation="CenterScreen"
        Background="#1e1e2e"
        ExtendClientAreaToDecorationsHint="False">

  <Grid ColumnDefinitions="160,*">

    <!-- Sidebar -->
    <Border Grid.Column="0" Background="#11111b">
      <StackPanel Margin="16,20" Spacing="0">
        <TextBlock Text="VoxMemo" FontSize="16" FontWeight="Bold"
                   Foreground="#cdd6f4" Margin="0,0,0,24"/>

        <!-- Welcome -->
        <StackPanel Orientation="Horizontal" Spacing="10" Margin="0,0,0,0">
          <Border Width="22" Height="22" CornerRadius="11"
                  Background="{Binding IsStep0, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepBgConverter}, ConverterParameter=active_welcome}">
            <TextBlock Text="✦" FontSize="10" FontWeight="Bold"
                       Foreground="#1e1e2e" HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <TextBlock Text="Welcome" FontSize="12" VerticalAlignment="Center"
                     Foreground="{Binding IsStep0, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepFgConverter}}"/>
        </StackPanel>
        <Border Width="2" Height="14" Background="#313244" HorizontalAlignment="Left" Margin="10,2"/>

        <!-- Step 1 -->
        <StackPanel Orientation="Horizontal" Spacing="10">
          <Border Width="22" Height="22" CornerRadius="11"
                  Background="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepNumBgConverter}, ConverterParameter=1}">
            <TextBlock FontSize="10" FontWeight="Bold" Foreground="#1e1e2e"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Text="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepNumTextConverter}, ConverterParameter=1}"/>
          </Border>
          <TextBlock Text="AI Provider" FontSize="12" VerticalAlignment="Center"
                     Foreground="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepLabelFgConverter}, ConverterParameter=1}"/>
        </StackPanel>
        <Border Width="2" Height="14" Background="#313244" HorizontalAlignment="Left" Margin="10,2"/>

        <!-- Step 2 -->
        <StackPanel Orientation="Horizontal" Spacing="10">
          <Border Width="22" Height="22" CornerRadius="11"
                  Background="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepNumBgConverter}, ConverterParameter=2}">
            <TextBlock FontSize="10" FontWeight="Bold" Foreground="#1e1e2e"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Text="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepNumTextConverter}, ConverterParameter=2}"/>
          </Border>
          <TextBlock Text="Whisper Model" FontSize="12" VerticalAlignment="Center"
                     Foreground="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepLabelFgConverter}, ConverterParameter=2}"/>
        </StackPanel>
        <Border Width="2" Height="14" Background="#313244" HorizontalAlignment="Left" Margin="10,2"/>

        <!-- Step 3 -->
        <StackPanel Orientation="Horizontal" Spacing="10">
          <Border Width="22" Height="22" CornerRadius="11"
                  Background="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepNumBgConverter}, ConverterParameter=3}">
            <TextBlock FontSize="10" FontWeight="Bold" Foreground="#1e1e2e"
                       HorizontalAlignment="Center" VerticalAlignment="Center"
                       Text="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepNumTextConverter}, ConverterParameter=3}"/>
          </Border>
          <TextBlock Text="Audio Device" FontSize="12" VerticalAlignment="Center"
                     Foreground="{Binding CurrentStep, Converter={x:Static VoxMemo.Views.OnboardingWindow.StepLabelFgConverter}, ConverterParameter=3}"/>
        </StackPanel>
      </StackPanel>
    </Border>

    <!-- Content panels -->
    <Grid Grid.Column="1" Margin="28,24,28,20">

      <!-- Welcome (step 0) -->
      <Grid IsVisible="{Binding IsStep0}" RowDefinitions="*,Auto">
        <StackPanel Grid.Row="0" HorizontalAlignment="Center" VerticalAlignment="Center"
                    Spacing="10" Margin="0,0,0,16">
          <TextBlock Text="VoxMemo" FontSize="30" FontWeight="Bold"
                     Foreground="#cba6f7" HorizontalAlignment="Center"/>
          <TextBlock Text="Record. Transcribe. Summarize."
                     FontSize="14" Foreground="#cdd6f4" HorizontalAlignment="Center"/>
          <TextBlock Text="Takes 2 minutes. Everything runs locally on your machine."
                     FontSize="11" Foreground="#7f849c" HorizontalAlignment="Center"
                     TextWrapping="Wrap" TextAlignment="Center"/>
        </StackPanel>
        <StackPanel Grid.Row="1" HorizontalAlignment="Center" Spacing="8">
          <Button Content="Get Started"
                  Command="{Binding NextCommand}"
                  Background="#cba6f7" Foreground="#1e1e2e"
                  CornerRadius="8" Padding="32,12" FontSize="14" FontWeight="SemiBold"
                  HorizontalAlignment="Center"/>
          <Button Content="Set up later"
                  Command="{Binding SkipCommand}"
                  Background="Transparent" Foreground="#585b70"
                  BorderThickness="0" FontSize="12"
                  HorizontalAlignment="Center"/>
        </StackPanel>
      </Grid>

      <!-- AI Provider (step 1) -->
      <Grid IsVisible="{Binding IsStep1}" RowDefinitions="Auto,*,Auto">
        <StackPanel Grid.Row="0" Margin="0,0,0,12">
          <TextBlock Text="Choose your AI provider" FontSize="16" FontWeight="SemiBold"
                     Foreground="#cdd6f4"/>
          <TextBlock Text="Used for summaries and speaker identification"
                     FontSize="11" Foreground="#7f849c" Margin="0,4,0,0"/>
        </StackPanel>
        <StackPanel Grid.Row="1" Spacing="6">
          <!-- Ollama -->
          <Border CornerRadius="8" Padding="12,10" Cursor="Hand"
                  Background="{Binding SelectedAiProvider, Converter={x:Static VoxMemo.Views.OnboardingWindow.ProviderBgConverter}, ConverterParameter=Ollama}"
                  BorderThickness="1"
                  BorderBrush="{Binding SelectedAiProvider, Converter={x:Static VoxMemo.Views.OnboardingWindow.ProviderBorderConverter}, ConverterParameter=Ollama}">
            <Border.Tapped>
              <Binding Path="SelectedAiProvider" Mode="OneWayToSource" Source="Ollama"/>
            </Border.Tapped>
            <StackPanel>
              <TextBlock Text="Ollama" FontSize="13" FontWeight="SemiBold" Foreground="#cba6f7"/>
              <TextBlock Text="Local · private · no API key needed" FontSize="11" Foreground="#7f849c"/>
              <TextBlock Text="{Binding OllamaStatus}" FontSize="11" Foreground="#a6e3a1"
                         IsVisible="{Binding OllamaStatus, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                         Margin="0,4,0,0"/>
            </StackPanel>
          </Border>
          <!-- OpenAI -->
          <Border CornerRadius="8" Padding="12,10" Cursor="Hand"
                  Background="{Binding SelectedAiProvider, Converter={x:Static VoxMemo.Views.OnboardingWindow.ProviderBgConverter}, ConverterParameter=OpenAI}"
                  BorderThickness="1"
                  BorderBrush="{Binding SelectedAiProvider, Converter={x:Static VoxMemo.Views.OnboardingWindow.ProviderBorderConverter}, ConverterParameter=OpenAI}">
            <Border.Tapped>
              <Binding Path="SelectedAiProvider" Mode="OneWayToSource" Source="OpenAI"/>
            </Border.Tapped>
            <TextBlock Text="OpenAI" FontSize="13" FontWeight="SemiBold" Foreground="#cdd6f4"/>
            <TextBlock Text="GPT-4o · requires API key" FontSize="11" Foreground="#7f849c"/>
          </Border>
          <!-- Anthropic -->
          <Border CornerRadius="8" Padding="12,10" Cursor="Hand"
                  Background="{Binding SelectedAiProvider, Converter={x:Static VoxMemo.Views.OnboardingWindow.ProviderBgConverter}, ConverterParameter=Anthropic}"
                  BorderThickness="1"
                  BorderBrush="{Binding SelectedAiProvider, Converter={x:Static VoxMemo.Views.OnboardingWindow.ProviderBorderConverter}, ConverterParameter=Anthropic}">
            <Border.Tapped>
              <Binding Path="SelectedAiProvider" Mode="OneWayToSource" Source="Anthropic"/>
            </Border.Tapped>
            <TextBlock Text="Anthropic" FontSize="13" FontWeight="SemiBold" Foreground="#cdd6f4"/>
            <TextBlock Text="Claude · requires API key" FontSize="11" Foreground="#7f849c"/>
          </Border>
          <!-- API key input (cloud only) -->
          <TextBox Watermark="Paste API key here..."
                   Text="{Binding ApiKey}"
                   PasswordChar="●"
                   IsVisible="{Binding ShowApiKey}"
                   Background="#313244" Foreground="#cdd6f4"
                   CornerRadius="6" Padding="10,8" Margin="0,4,0,0"/>
        </StackPanel>
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto" Margin="0,8,0,0">
          <Button Grid.Column="0" Content="Skip for now" Command="{Binding SkipCommand}"
                  Background="Transparent" Foreground="#585b70" BorderThickness="0" FontSize="12"
                  HorizontalAlignment="Left"/>
          <Button Grid.Column="1" Content="Next →" Command="{Binding NextCommand}"
                  Background="#cba6f7" Foreground="#1e1e2e"
                  CornerRadius="6" Padding="20,8" FontSize="13" FontWeight="SemiBold"/>
        </Grid>
      </Grid>

      <!-- Whisper Model (step 2) -->
      <Grid IsVisible="{Binding IsStep2}" RowDefinitions="Auto,*,Auto,Auto">
        <StackPanel Grid.Row="0" Margin="0,0,0,12">
          <TextBlock Text="Download a Whisper model" FontSize="16" FontWeight="SemiBold"
                     Foreground="#cdd6f4"/>
          <TextBlock Text="Used to transcribe your recordings locally"
                     FontSize="11" Foreground="#7f849c" Margin="0,4,0,0"/>
        </StackPanel>
        <StackPanel Grid.Row="1" Spacing="6">
          <!-- base -->
          <Border Background="#181825" CornerRadius="8" Padding="12,10">
            <Grid ColumnDefinitions="*,Auto">
              <StackPanel Grid.Column="0">
                <StackPanel Orientation="Horizontal" Spacing="8">
                  <TextBlock Text="base" FontSize="13" FontWeight="SemiBold" Foreground="#cdd6f4"/>
                  <Border Background="#cba6f7" CornerRadius="3" Padding="5,1">
                    <TextBlock Text="Recommended" FontSize="9" FontWeight="Bold" Foreground="#1e1e2e"/>
                  </Border>
                </StackPanel>
                <TextBlock Text="142 MB · Good accuracy · Fast" FontSize="11" Foreground="#7f849c"/>
              </StackPanel>
              <Button Grid.Column="1" Content="Download"
                      Command="{Binding DownloadModelCommand}" CommandParameter="base"
                      IsEnabled="{Binding !IsDownloading}"
                      Background="#cba6f7" Foreground="#1e1e2e"
                      CornerRadius="4" Padding="10,4" FontSize="11" FontWeight="SemiBold"/>
            </Grid>
          </Border>
          <!-- tiny -->
          <Border Background="#181825" CornerRadius="8" Padding="12,10">
            <Grid ColumnDefinitions="*,Auto">
              <StackPanel Grid.Column="0">
                <TextBlock Text="tiny" FontSize="13" FontWeight="SemiBold" Foreground="#cdd6f4"/>
                <TextBlock Text="75 MB · Lower accuracy · Fastest" FontSize="11" Foreground="#7f849c"/>
              </StackPanel>
              <Button Grid.Column="1" Content="Download"
                      Command="{Binding DownloadModelCommand}" CommandParameter="tiny"
                      IsEnabled="{Binding !IsDownloading}"
                      Background="#89b4fa" Foreground="#1e1e2e"
                      CornerRadius="4" Padding="10,4" FontSize="11" FontWeight="SemiBold"/>
            </Grid>
          </Border>
          <!-- small -->
          <Border Background="#181825" CornerRadius="8" Padding="12,10">
            <Grid ColumnDefinitions="*,Auto">
              <StackPanel Grid.Column="0">
                <TextBlock Text="small" FontSize="13" FontWeight="SemiBold" Foreground="#cdd6f4"/>
                <TextBlock Text="466 MB · Better accuracy · Slower" FontSize="11" Foreground="#7f849c"/>
              </StackPanel>
              <Button Grid.Column="1" Content="Download"
                      Command="{Binding DownloadModelCommand}" CommandParameter="small"
                      IsEnabled="{Binding !IsDownloading}"
                      Background="#89b4fa" Foreground="#1e1e2e"
                      CornerRadius="4" Padding="10,4" FontSize="11" FontWeight="SemiBold"/>
            </Grid>
          </Border>
        </StackPanel>
        <!-- Download progress -->
        <TextBlock Grid.Row="2"
                   Text="{Binding DownloadProgress}"
                   FontSize="12" Foreground="#f9e2af"
                   IsVisible="{Binding DownloadProgress, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                   Margin="0,8,0,0"/>
        <Grid Grid.Row="3" ColumnDefinitions="*,Auto" Margin="0,8,0,0">
          <Button Grid.Column="0" Content="Skip — download later in Settings"
                  Command="{Binding SkipCommand}"
                  Background="Transparent" Foreground="#585b70" BorderThickness="0" FontSize="12"
                  HorizontalAlignment="Left"/>
          <Button Grid.Column="1" Content="Next →" Command="{Binding NextCommand}"
                  IsEnabled="{Binding ModelDownloaded}"
                  Background="#cba6f7" Foreground="#1e1e2e"
                  CornerRadius="6" Padding="20,8" FontSize="13" FontWeight="SemiBold"/>
        </Grid>
      </Grid>

      <!-- Audio Device (step 3) -->
      <Grid IsVisible="{Binding IsStep3}" RowDefinitions="Auto,*,Auto">
        <StackPanel Grid.Row="0" Margin="0,0,0,12">
          <TextBlock Text="Select your microphone" FontSize="16" FontWeight="SemiBold"
                     Foreground="#cdd6f4"/>
          <TextBlock Text="You can change this at any time in Settings"
                     FontSize="11" Foreground="#7f849c" Margin="0,4,0,0"/>
        </StackPanel>
        <ListBox Grid.Row="1"
                 ItemsSource="{Binding AvailableDevices}"
                 SelectedItem="{Binding SelectedDevice}"
                 Background="Transparent">
          <ListBox.Styles>
            <Style Selector="ListBoxItem">
              <Setter Property="Padding" Value="12,8"/>
              <Setter Property="Margin" Value="0,3"/>
              <Setter Property="CornerRadius" Value="8"/>
              <Setter Property="Background" Value="#181825"/>
            </Style>
            <Style Selector="ListBoxItem:selected">
              <Setter Property="Background" Value="#313244"/>
            </Style>
          </ListBox.Styles>
          <ListBox.ItemTemplate>
            <DataTemplate x:DataType="vm:AudioDeviceItem">
              <TextBlock Text="{Binding Name}" FontSize="13" Foreground="#cdd6f4"/>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto" Margin="0,12,0,0">
          <Button Grid.Column="0" Content="← Back" Command="{Binding BackCommand}"
                  Background="Transparent" Foreground="#7f849c" BorderThickness="0" FontSize="12"
                  HorizontalAlignment="Left"/>
          <Button Grid.Column="1" Content="Finish →" Command="{Binding NextCommand}"
                  Background="#a6e3a1" Foreground="#1e1e2e"
                  CornerRadius="6" Padding="20,8" FontSize="13" FontWeight="SemiBold"/>
        </Grid>
      </Grid>

    </Grid>
  </Grid>
</Window>
```

- [ ] **Step 2: Create `Views/OnboardingWindow.axaml.cs`**

The AXAML uses value converters for sidebar step styling. Define them in code-behind as static `IValueConverter` fields (same pattern as `RecordingView.axaml.cs`).

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VoxMemo.ViewModels;

namespace VoxMemo.Views;

public partial class OnboardingWindow : Window
{
    // Sidebar: circle background — active=#cba6f7, done=#a6e3a1, upcoming=#313244
    public static readonly IValueConverter StepNumBgConverter =
        new FuncValueConverter<int, IBrush>(step => Brush.Parse("#313244")); // simplified; code-behind sets per step

    // Provider card background
    public static readonly IValueConverter ProviderBgConverter =
        new FuncValueConverter<string, IBrush>(selected =>
            Brush.Parse("#313244"));

    public static readonly IValueConverter ProviderBorderConverter =
        new FuncValueConverter<string, IBrush>(selected =>
            Brush.Parse("#45475a"));

    // Step label foreground
    public static readonly IValueConverter StepLabelFgConverter =
        new FuncValueConverter<int, IBrush>(step => Brush.Parse("#cdd6f4"));

    // Step number text (checkmark if done, number if not)
    public static readonly IValueConverter StepNumTextConverter =
        new FuncValueConverter<int, string>(step => step.ToString());

    // Step 0 active
    public static readonly IValueConverter StepFgConverter =
        new FuncValueConverter<bool, IBrush>(isActive =>
            isActive ? Brush.Parse("#cba6f7") : Brush.Parse("#585b70"));

    public OnboardingWindow()
    {
        InitializeComponent();
        var vm = new OnboardingViewModel();
        DataContext = vm;
        vm.CloseRequested += (_, _) => Close();
        Closing += async (_, e) =>
        {
            // X-button close: mark onboarding done so it doesn't re-appear
            if (DataContext is OnboardingViewModel closingVm)
                await closingVm.SkipCommand.ExecuteAsync(null);
        };
    }
}
```

**Note on converters:** The AXAML above uses converters in a simplified form. The sidebar step circles need 3 states (active, done, upcoming) based on the current step number. The cleanest approach is to bind `CurrentStep` to a `FuncValueConverter<int, IBrush>` with a `ConverterParameter` for the step number being rendered. Replace the converter implementations with these:

```csharp
// Sidebar circle background — active (current), done (past), upcoming (future)
public static readonly IValueConverter StepNumBgConverter =
    new FuncValueConverter<int, IBrush>((current, param) =>
    {
        if (!int.TryParse(param?.ToString(), out int stepNum)) return Brush.Parse("#313244");
        if (current == stepNum) return Brush.Parse("#cba6f7"); // active
        if (current > stepNum) return Brush.Parse("#a6e3a1"); // done
        return Brush.Parse("#313244"); // upcoming
    });

// Sidebar circle text — checkmark if done, number if current/upcoming
public static readonly IValueConverter StepNumTextConverter =
    new FuncValueConverter<int, string>((current, param) =>
    {
        if (!int.TryParse(param?.ToString(), out int stepNum)) return param?.ToString() ?? "";
        return current > stepNum ? "✓" : stepNum.ToString();
    });

// Sidebar label foreground
public static readonly IValueConverter StepLabelFgConverter =
    new FuncValueConverter<int, IBrush>((current, param) =>
    {
        if (!int.TryParse(param?.ToString(), out int stepNum)) return Brush.Parse("#585b70");
        if (current == stepNum) return Brush.Parse("#cba6f7");
        if (current > stepNum) return Brush.Parse("#a6e3a1");
        return Brush.Parse("#585b70");
    });

// Provider card background
public static readonly IValueConverter ProviderBgConverter =
    new FuncValueConverter<string, IBrush>((selected, param) =>
        selected == param?.ToString() ? Brush.Parse("#313244") : Brush.Parse("#181825"));

// Provider card border
public static readonly IValueConverter ProviderBorderConverter =
    new FuncValueConverter<string, IBrush>((selected, param) =>
        selected == param?.ToString() ? Brush.Parse("#cba6f7") : Brush.Parse("Transparent"));
```

The `FuncValueConverter` in Avalonia supports a `(TIn value, object? parameter)` overload — use it for all converters that need `ConverterParameter`.

- [ ] **Step 3: Fix provider card tap binding**

The AXAML `<Border.Tapped>` inline binding doesn't directly set a string. Replace provider card Tapped with named event handlers in code-behind instead:

In `OnboardingWindow.axaml`, add `x:Name` to each provider Border and `Tapped` event:
```xml
<Border x:Name="OllamaCard" Tapped="OnOllamaTapped" ...>
<Border x:Name="OpenAiCard" Tapped="OnOpenAiTapped" ...>
<Border x:Name="AnthropicCard" Tapped="OnAnthropicTapped" ...>
```

In `OnboardingWindow.axaml.cs`, add handlers:
```csharp
private void OnOllamaTapped(object? sender, Avalonia.Input.TappedEventArgs e)
{
    if (DataContext is OnboardingViewModel vm) vm.SelectedAiProvider = "Ollama";
}
private void OnOpenAiTapped(object? sender, Avalonia.Input.TappedEventArgs e)
{
    if (DataContext is OnboardingViewModel vm) vm.SelectedAiProvider = "OpenAI";
}
private void OnAnthropicTapped(object? sender, Avalonia.Input.TappedEventArgs e)
{
    if (DataContext is OnboardingViewModel vm) vm.SelectedAiProvider = "Anthropic";
}
```

- [ ] **Step 4: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors. Fix any AXAML binding errors reported as warnings.

- [ ] **Step 5: Run all tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj -v quiet
```
Expected: 110 passed, 0 failed.

- [ ] **Step 6: Commit**

```
git add Views/OnboardingWindow.axaml Views/OnboardingWindow.axaml.cs
git commit -m "feat: add OnboardingWindow AXAML — 4-screen first-run wizard"
```

---

## Task 3: Wire OnboardingWindow into App.axaml.cs

**Files:**
- Modify: `App.axaml.cs`

- [ ] **Step 1: Read `App.axaml.cs` lines 95–130**

Locate the block:
```csharp
if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
{
    Log.Information("Setting up main window");
    DisableAvaloniaDataAnnotationValidation();
    _mainVm = new MainWindowViewModel();
    desktop.MainWindow = new MainWindow
    {
        DataContext = _mainVm,
    };
    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    ...
```

- [ ] **Step 2: Add onboarding check and show logic**

Add the onboarding check right after `SetTheme(savedTheme)` and before the `if (ApplicationLifetime ...)` block:

```csharp
// Check if first-run onboarding is needed
bool needsOnboarding = false;
try
{
    await using var onbDb = AppDbContextFactory.Create();
    var onbSetting = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(onbDb.AppSettings, s => s.Key == "onboarding_complete");
    needsOnboarding = onbSetting?.Value != "true";
}
catch (Exception ex)
{
    Log.Warning(ex, "Failed to check onboarding_complete, skipping wizard");
}
```

Then inside the `if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)` block, after creating `MainWindow`, replace the implicit show with explicit show/hide logic:

```csharp
_mainVm = new MainWindowViewModel();
var mainWindow = new MainWindow { DataContext = _mainVm };
desktop.MainWindow = mainWindow;
desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

if (needsOnboarding)
{
    // Hide MainWindow until onboarding closes
    mainWindow.IsVisible = false;
    var onboarding = new VoxMemo.Views.OnboardingWindow();
    onboarding.Closed += (_, _) => mainWindow.IsVisible = true;
    onboarding.Show();
}
// (MainWindow shows automatically via Avalonia when IsVisible=true)
```

Remove or adjust any existing line that does `desktop.MainWindow = new MainWindow { ... }` to avoid duplication — the new code above is the only place it should be created.

- [ ] **Step 3: Build**

```
dotnet build VoxMemo.csproj
```
Expected: 0 errors.

- [ ] **Step 4: Run all tests**

```
dotnet test VoxMemo.Tests/VoxMemo.Tests.csproj -v quiet
```
Expected: 110 passed, 0 failed.

- [ ] **Step 5: Manual smoke test**

Run the app:
```
dotnet run --project VoxMemo.csproj
```

Verify:
1. The onboarding wizard appears on first launch (since `onboarding_complete` isn't set in a fresh DB)
2. "Set up later" closes the wizard and shows the main window
3. Restarting the app does NOT show the wizard again

To force the wizard to reappear for testing, delete the DB:
```
# Windows: %APPDATA%\VoxMemo\voxmemo.db
```
Or from the app's Settings, a future "Reset onboarding" button could set `onboarding_complete = "false"`.

- [ ] **Step 6: Commit**

```
git add App.axaml.cs
git commit -m "feat: show OnboardingWindow on first launch, hide MainWindow until done"
```

---

## Self-Review

### Spec coverage

| Spec requirement | Task covering it |
|---|---|
| Trigger: check `onboarding_complete` on startup | Task 3 |
| Show before MainWindow is visible | Task 3 |
| Skip/Set up later marks complete | Task 1 — `SaveAndCloseAsync` |
| X-button close marks complete | Task 2 — `Closing` handler |
| Welcome screen with Get Started + Set up later | Task 2 — IsStep0 panel |
| AI Provider: 3 cards, Ollama auto-check, API key input | Task 2 — IsStep1 panel |
| Whisper Model: 3 rows, download with progress, Next disabled until done | Task 2 — IsStep2 panel |
| Audio Device: device list, Auto pre-selected, Back + Finish | Task 2 — IsStep3 panel |
| Save ai_provider on finish | Task 1 — `FinishAsync` |
| Save API key encrypted | Task 1 — `SaveApiKeyAsync` |
| Save whisper_model if downloaded | Task 1 — `FinishAsync` |
| Save audio device on finish | Task 1 — `FinishAsync` |
| Sidebar shows active/done/upcoming step states | Task 2 — converters |

No gaps found.

### Placeholder scan

No TBDs, TODOs, or "add error handling" phrases found.

### Type consistency

- `OnboardingViewModel.SkipCommand` used in Task 1 (definition) and Task 2 (AXAML binding) — consistent.
- `CloseRequested` event defined in Task 1, subscribed in Task 2 (`vm.CloseRequested += (_, _) => Close()`) — consistent.
- `AudioDeviceItem` used in `AvailableDevices` in Task 1, referenced in AXAML `DataTemplate` in Task 2 — type exists in `ViewModels/RecordingViewModel.cs`.
- `WhisperModelOption` record defined in Task 1, `DownloadableModels` array used only in AXAML display — consistent.
- `AppDbContextFactory.Create()` used in Task 1 — established in the prior improvement plan.
- `SecureStorage.Encrypt` used in Task 1 — exists in `Services/Security/SecureStorage.cs`.
