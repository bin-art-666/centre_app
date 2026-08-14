using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace centre_app;

public sealed class LauncherItemData
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string? CustomIconPath { get; set; }

    [JsonIgnore]
    public BitmapSource? Icon { get; set; }
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

    public LauncherSettings Clone() => new()
    {
        FloatingSearchMode = FloatingSearchMode,
        FullScreen = FullScreen,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        Columns = Columns,
        Rows = Rows,
        IconSize = IconSize
    };

    public void Normalize(double maxWidth, double maxHeight)
    {
        WindowWidth = Math.Clamp(WindowWidth, Math.Min(800, maxWidth), maxWidth);
        WindowHeight = Math.Clamp(WindowHeight, Math.Min(600, maxHeight), maxHeight);
        Columns = Math.Clamp(Columns, 4, 12);
        Rows = Math.Clamp(Rows, 3, 8);
        IconSize = Math.Clamp(IconSize, 48, 128);
    }
}
