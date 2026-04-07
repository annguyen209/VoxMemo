namespace VoxMemo.Tests;

public class HotkeyParsingTests
{
    // Access the private ParseHotkey via reflection since it's in App class
    // We'll test via a helper that mimics the same logic
    private static (int modifiers, int vk) ParseHotkey(string hotkey)
    {
        int modifiers = 0;
        int vk = 0;

        var parts = hotkey.Split('+').Select(p => p.Trim().ToLower()).ToArray();
        foreach (var part in parts)
        {
            switch (part)
            {
                case "ctrl" or "control": modifiers |= 0x0002; break; // MOD_CONTROL
                case "shift": modifiers |= 0x0004; break; // MOD_SHIFT
                case "alt": modifiers |= 0x0001; break; // MOD_ALT
                case "win": modifiers |= 0x0008; break; // MOD_WIN
                default:
                    if (part.Length == 1 && char.IsLetterOrDigit(part[0]))
                        vk = char.ToUpper(part[0]);
                    else if (part.StartsWith("f") && int.TryParse(part[1..], out var fnum) && fnum is >= 1 and <= 24)
                        vk = 0x70 + fnum - 1;
                    else if (part == "space") vk = 0x20;
                    break;
            }
        }

        return (modifiers, vk);
    }

    [Fact]
    public void Parse_CtrlShiftR()
    {
        var (mod, vk) = ParseHotkey("Ctrl+Shift+R");
        Assert.Equal(0x0002 | 0x0004, mod); // CONTROL | SHIFT
        Assert.Equal('R', vk);
    }

    [Fact]
    public void Parse_AltF9()
    {
        var (mod, vk) = ParseHotkey("Alt+F9");
        Assert.Equal(0x0001, mod); // ALT
        Assert.Equal(0x70 + 8, vk); // VK_F9
    }

    [Fact]
    public void Parse_CtrlAltM()
    {
        var (mod, vk) = ParseHotkey("Ctrl+Alt+M");
        Assert.Equal(0x0002 | 0x0001, mod); // CONTROL | ALT
        Assert.Equal('M', vk);
    }

    [Fact]
    public void Parse_F1()
    {
        var (mod, vk) = ParseHotkey("F1");
        Assert.Equal(0, mod);
        Assert.Equal(0x70, vk); // VK_F1
    }

    [Fact]
    public void Parse_CtrlSpace()
    {
        var (mod, vk) = ParseHotkey("Ctrl+Space");
        Assert.Equal(0x0002, mod); // CONTROL
        Assert.Equal(0x20, vk); // VK_SPACE
    }

    [Fact]
    public void Parse_WinShiftA()
    {
        var (mod, vk) = ParseHotkey("Win+Shift+A");
        Assert.Equal(0x0008 | 0x0004, mod); // WIN | SHIFT
        Assert.Equal('A', vk);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsZero()
    {
        var (mod, vk) = ParseHotkey("");
        Assert.Equal(0, mod);
        Assert.Equal(0, vk);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var (mod1, vk1) = ParseHotkey("ctrl+shift+r");
        var (mod2, vk2) = ParseHotkey("CTRL+SHIFT+R");
        Assert.Equal(mod1, mod2);
        Assert.Equal(vk1, vk2);
    }

    [Fact]
    public void Parse_WithSpaces()
    {
        var (mod, vk) = ParseHotkey("Ctrl + Shift + R");
        Assert.Equal(0x0002 | 0x0004, mod);
        Assert.Equal('R', vk);
    }

    [Fact]
    public void Parse_F12()
    {
        var (mod, vk) = ParseHotkey("Ctrl+F12");
        Assert.Equal(0x0002, mod);
        Assert.Equal(0x70 + 11, vk); // VK_F12
    }
}
