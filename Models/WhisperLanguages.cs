using System.Collections.Generic;

namespace VoxMemo.Models;

public static class WhisperLanguages
{
    public static readonly List<LanguageItem> All =
    [
        new("auto", "Auto-detect"),
        new("en", "English"),
        new("vi", "Vietnamese"),
        new("zh", "Chinese"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("fr", "French"),
        new("de", "German"),
        new("es", "Spanish"),
        new("pt", "Portuguese"),
        new("it", "Italian"),
        new("ru", "Russian"),
        new("ar", "Arabic"),
        new("hi", "Hindi"),
        new("th", "Thai"),
        new("id", "Indonesian"),
        new("tr", "Turkish"),
        new("nl", "Dutch"),
        new("pl", "Polish"),
        new("sv", "Swedish"),
        new("da", "Danish"),
        new("fi", "Finnish"),
        new("no", "Norwegian"),
        new("uk", "Ukrainian"),
        new("cs", "Czech"),
        new("el", "Greek"),
        new("ro", "Romanian"),
        new("hu", "Hungarian"),
        new("he", "Hebrew"),
        new("ms", "Malay"),
        new("tl", "Filipino"),
    ];
}

public record LanguageItem(string Code, string Name)
{
    public override string ToString() => $"{Name} ({Code})";
}
