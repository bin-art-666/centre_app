using System.IO;
using System.Text.Json;

namespace centre_app;

public static class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Centre");

    private static string ItemsPath => Path.Combine(RootDirectory, "items.json");
    private static string SettingsPath => Path.Combine(RootDirectory, "settings.json");
    private static string IconsDirectory => Path.Combine(RootDirectory, "Icons");

    public static List<LauncherItemData> LoadItems() =>
        LoadOrDefault(ItemsPath, static () => new List<LauncherItemData>());

    public static LauncherSettings LoadSettings() =>
        LoadOrDefault(SettingsPath, static () => new LauncherSettings());

    public static void SaveItems(IReadOnlyCollection<LauncherItemData> items) => Save(ItemsPath, items);
    public static void SaveSettings(LauncherSettings settings) => Save(SettingsPath, settings);

    public static string CopyCustomIcon(Guid itemId, string sourcePath)
    {
        Directory.CreateDirectory(IconsDirectory);
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var destination = Path.Combine(IconsDirectory, $"{itemId:N}{extension}");
        var temporaryPath = destination + ".tmp";
        File.Copy(sourcePath, temporaryPath, true);
        File.Move(temporaryPath, destination, true);
        foreach (var old in Directory.EnumerateFiles(IconsDirectory, $"{itemId:N}.*"))
        {
            if (!string.Equals(old, destination, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(old, temporaryPath, StringComparison.OrdinalIgnoreCase))
                File.Delete(old);
        }
        return destination;
    }

    public static void DeleteCustomIcon(LauncherItemData item)
    {
        if (string.IsNullOrWhiteSpace(item.CustomIconPath)) return;
        try
        {
            var fullPath = Path.GetFullPath(item.CustomIconPath);
            var iconRoot = Path.GetFullPath(IconsDirectory) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(iconRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch { }
    }

    private static T LoadOrDefault<T>(string path, Func<T> fallback)
    {
        try
        {
            if (!File.Exists(path)) return fallback();
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
            return value ?? fallback();
        }
        catch
        {
            BackupCorruptFile(path);
            return fallback();
        }
    }

    private static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(RootDirectory);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private static void BackupCorruptFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var backupPath = $"{path}.corrupt.{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(path, backupPath, true);
        }
        catch { }
    }
}
