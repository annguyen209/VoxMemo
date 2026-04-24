using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace VoxMemo.Views;

public partial class RecordingView : UserControl
{
    public static readonly IValueConverter LevelToWidthConverter =
        new FuncValueConverter<float, double>(level => level * 400);

    public static readonly IValueConverter PauseTextConverter =
        new FuncValueConverter<bool, string>(isPaused => isPaused ? "Resume" : "Pause");

    public static readonly IValueConverter CaptionStatusConverter =
        new FuncValueConverter<bool, string>(enabled =>
            enabled
                ? "Captions update every ~12 seconds using local Whisper"
                : "Enable live captions before recording to see real-time text");

    public static readonly IValueConverter DeviceLabelConverter =
        new FuncValueConverter<string?, string>(source => source switch
        {
            "Both (Mic + Speaker)" => "Microphone Device",
            "System Audio" => "Output Device",
            _ => "Device"
        });

    public static readonly IValueConverter SourceHintConverter =
        new FuncValueConverter<string?, string>(source => source switch
        {
            "System Audio" => "Records all sound playing on your computer (meeting audio, speakers, etc.)",
            "Both (Mic + Speaker)" => "Captures your mic + speaker output mixed together — best for meeting transcription",
            _ => "Records from your microphone (your voice only)"
        });

    public static readonly IValueConverter RecordSubTextConverter =
        new FuncValueConverter<bool, string>(isRecording => isRecording ? "Click to stop" : "Click to start");

    public RecordingView()
    {
        InitializeComponent();
    }
}
