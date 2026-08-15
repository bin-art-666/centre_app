using System.Text.Json;

namespace Centre.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"Centre.Tests.{Guid.NewGuid():N}");

    [Fact]
    public void NewSettingsUseBalancedDefaultIconSize()
    {
        var settings = new centre_app.LauncherSettings();
        Assert.Equal(64, settings.IconSize);
        Assert.False(settings.UseIconMask);
        Assert.False(settings.SoftwareRenderingCompatibility);
    }

    [Fact]
    public void LegacySettingsWithoutRenderingModeDefaultToHardwareAcceleration()
    {
        var settings = JsonSerializer.Deserialize<centre_app.LauncherSettings>("{\"Columns\":8}")!;

        Assert.False(settings.SoftwareRenderingCompatibility);
    }

    [Fact]
    public void StandaloneAltIsTheDefaultHotkey()
    {
        var settings = new centre_app.LauncherSettings();
        settings.Normalize(1440, 900);

        Assert.Equal(0u, settings.HotkeyModifiers);
        Assert.Equal(0x12, settings.HotkeyVirtualKey);
        Assert.Equal(1, settings.HotkeyDefaultsVersion);
    }

    [Fact]
    public void LegacyDefaultHotkeyMigratesToStandaloneAlt()
    {
        var settings = new centre_app.LauncherSettings
        {
            HotkeyModifiers = 0x0004,
            HotkeyVirtualKey = 0x09,
            HotkeyDefaultsVersion = 0
        };

        settings.Normalize(1440, 900);

        Assert.Equal(0u, settings.HotkeyModifiers);
        Assert.Equal(0x12, settings.HotkeyVirtualKey);
    }

    [Fact]
    public void LegacyItemsDefaultToFileTargets()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "items.json");
        File.WriteAllText(path, "[{\"Id\":\"13ac80e6-7bf2-47ac-b124-80cbfdffdd93\",\"Name\":\"Legacy\",\"TargetPath\":\"C:\\\\Legacy.exe\"}]");
        var items = centre_app.AppDataStore.LoadOrDefault(path, () => new List<centre_app.LauncherItemData>());
        Assert.Single(items);
        Assert.Equal(centre_app.LauncherTargetKind.File, items[0].TargetKind);
    }

    [Fact]
    public void AtomicSaveKeepsPreviousVersionAsBackup()
    {
        var path = Path.Combine(_directory, "settings.json");
        centre_app.AppDataStore.Save(path, new centre_app.LauncherSettings { Columns = 7 });
        centre_app.AppDataStore.Save(path, new centre_app.LauncherSettings { Columns = 9 });
        centre_app.AppDataStore.Save(path, new centre_app.LauncherSettings { Columns = 11 });
        Assert.True(File.Exists(path + ".bak"));
        Assert.Equal(11, JsonSerializer.Deserialize<centre_app.LauncherSettings>(File.ReadAllText(path))!.Columns);
        Assert.Equal(9, JsonSerializer.Deserialize<centre_app.LauncherSettings>(File.ReadAllText(path + ".bak"))!.Columns);
    }

    [Fact]
    public void CorruptPrimaryRecoversFromBackup()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "settings.json");
        File.WriteAllText(path, "not-json");
        File.WriteAllText(path + ".bak", JsonSerializer.Serialize(new centre_app.LauncherSettings { Rows = 6 }));
        var settings = centre_app.AppDataStore.LoadOrDefault(path, () => new centre_app.LauncherSettings());
        Assert.Equal(6, settings.Rows);
        Assert.NotEmpty(Directory.EnumerateFiles(_directory, "settings.json.corrupt.*"));
    }

    [Fact]
    public void DisplayAreaAndStaticBackgroundSurviveCloneAndNormalize()
    {
        var settings = new centre_app.LauncherSettings
        {
            AppAreaWidth = 5000,
            AppAreaHeight = 100,
            EnablePinyinSearch = true,
            StaticBlackBackground = true,
            UseIconMask = true,
            SoftwareRenderingCompatibility = true
        };

        settings.Normalize(1440, 900);
        var clone = settings.Clone();

        Assert.Equal(1440, clone.AppAreaWidth);
        Assert.Equal(420, clone.AppAreaHeight);
        Assert.True(clone.EnablePinyinSearch);
        Assert.True(clone.StaticBlackBackground);
        Assert.True(clone.UseIconMask);
        Assert.True(clone.SoftwareRenderingCompatibility);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }
}
