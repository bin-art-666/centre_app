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
    public double AppAreaWidth { get; set; } = 1200;
    public double AppAreaHeight { get; set; } = 760;
    public int Columns { get; set; } = 8;
    public int Rows { get; set; } = 5;
    public double IconSize { get; set; } = 64;
    public bool UseIconMask { get; set; }
    public bool EnablePinyinSearch { get; set; }
    public bool StaticBlackBackground { get; set; }
    public bool SoftwareRenderingCompatibility { get; set; }
    public double BackgroundBlur { get; set; } = 18;
    public uint HotkeyModifiers { get; set; }
    public int HotkeyVirtualKey { get; set; } = 0x12;
    public int HotkeyDefaultsVersion { get; set; }
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTimeOffset? LastUpdateCheckUtc { get; set; }
    public string? DismissedUpdateVersion { get; set; }

    public LauncherSettings Clone() => new()
    {
        FloatingSearchMode = FloatingSearchMode,
        FullScreen = FullScreen,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        AppAreaWidth = AppAreaWidth,
        AppAreaHeight = AppAreaHeight,
        Columns = Columns,
        Rows = Rows,
        IconSize = IconSize,
        UseIconMask = UseIconMask,
        EnablePinyinSearch = EnablePinyinSearch,
        StaticBlackBackground = StaticBlackBackground,
        SoftwareRenderingCompatibility = SoftwareRenderingCompatibility,
        BackgroundBlur = BackgroundBlur,
        HotkeyModifiers = HotkeyModifiers,
        HotkeyVirtualKey = HotkeyVirtualKey,
        HotkeyDefaultsVersion = HotkeyDefaultsVersion,
        AutoCheckUpdates = AutoCheckUpdates,
        LastUpdateCheckUtc = LastUpdateCheckUtc,
        DismissedUpdateVersion = DismissedUpdateVersion
    };

    public void Normalize(double maxWidth, double maxHeight)
    {
        WindowWidth = Math.Clamp(WindowWidth, Math.Min(800, maxWidth), maxWidth);
        WindowHeight = Math.Clamp(WindowHeight, Math.Min(600, maxHeight), maxHeight);
        AppAreaWidth = Math.Clamp(AppAreaWidth, Math.Min(600, maxWidth), maxWidth);
        AppAreaHeight = Math.Clamp(AppAreaHeight, Math.Min(420, maxHeight), maxHeight);
        Columns = Math.Clamp(Columns, 4, 12);
        Rows = Math.Clamp(Rows, 3, 8);
        IconSize = Math.Clamp(IconSize, 48, 128);
        BackgroundBlur = Math.Clamp(BackgroundBlur, 0, 40);
        if (HotkeyDefaultsVersion < 1)
        {
            if (HotkeyModifiers == 0x0004 && HotkeyVirtualKey == 0x09)
            {
                HotkeyModifiers = 0;
                HotkeyVirtualKey = 0x12;
            }
            HotkeyDefaultsVersion = 1;
        }
        var isStandaloneAlt = HotkeyModifiers == 0 && HotkeyVirtualKey == 0x12;
        if (!isStandaloneAlt && (HotkeyModifiers == 0 || HotkeyVirtualKey is < 0x08 or > 0xFE))
        {
            HotkeyModifiers = 0;
            HotkeyVirtualKey = 0x12;
        }
    }
}
