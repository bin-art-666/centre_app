using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace centre_app;

public enum LauncherTargetKind
{
    File,
    PackagedApp
}

public sealed class LauncherItemData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public LauncherTargetKind TargetKind { get; set; }
    public string TargetPath { get; set; } = string.Empty;
    public string? AppUserModelId { get; set; }
    public string? PackageFamilyName { get; set; }
    public string? CustomIconPath { get; set; }
    public int LaunchCount { get; set; }
    public DateTimeOffset? LastLaunchedUtc { get; set; }

    [JsonIgnore]
    public BitmapSource? Icon { get; set; }

    [JsonIgnore]
    public string SearchPinyin { get; set; } = string.Empty;

    [JsonIgnore]
    public string SearchInitials { get; set; } = string.Empty;
}

public sealed class LauncherSettings
{
    public bool FloatingSearchMode { get; set; }
    public bool FullScreen { get; set; } = true;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 760;
    public int Columns { get; set; } = 8;
    public int Rows { get; set; } = 5;
    public double IconSize { get; set; } = 76;
    public double BackgroundBlur { get; set; } = 18;
    public uint HotkeyModifiers { get; set; } = 0x0004;
    public int HotkeyVirtualKey { get; set; } = 0x09;
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public string? DismissedUpdateVersion { get; set; }

    public LauncherSettings Clone() => new()
    {
        FloatingSearchMode = FloatingSearchMode,
        FullScreen = FullScreen,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        Columns = Columns,
        Rows = Rows,
        IconSize = IconSize,
        BackgroundBlur = BackgroundBlur,
        HotkeyModifiers = HotkeyModifiers,
        HotkeyVirtualKey = HotkeyVirtualKey,
        AutoCheckUpdates = AutoCheckUpdates,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
        DismissedUpdateVersion = DismissedUpdateVersion
    };

    public void Normalize(double maxWidth, double maxHeight)
    {
        WindowWidth = Math.Clamp(WindowWidth, Math.Min(800, maxWidth), maxWidth);
        WindowHeight = Math.Clamp(WindowHeight, Math.Min(600, maxHeight), maxHeight);
        Columns = Math.Clamp(Columns, 4, 12);
        Rows = Math.Clamp(Rows, 3, 8);
        IconSize = Math.Clamp(IconSize, 48, 128);
        BackgroundBlur = Math.Clamp(BackgroundBlur, 0, 40);
        if (HotkeyModifiers == 0 || HotkeyVirtualKey is < 0x08 or > 0xFE)
        {
            HotkeyModifiers = 0x0004;
            HotkeyVirtualKey = 0x09;
        }
    }
}
