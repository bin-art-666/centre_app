namespace Centre.Tests;

public sealed class ServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"Centre.Icon.{Guid.NewGuid():N}.exe");

    [Fact]
    public async Task IconFingerprintChangesWhenSourceChanges()
    {
        await File.WriteAllTextAsync(_file, "one");
        var item = new centre_app.LauncherItemData { Name = "Test", TargetPath = _file };
        var first = await centre_app.IconCacheService.GetFingerprintAsync(item);
        await File.AppendAllTextAsync(_file, "two");
        File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddSeconds(2));
        var second = await centre_app.IconCacheService.GetFingerprintAsync(item);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("v1.2.0", 1, 1, 9, true)]
    [InlineData("1.0.0", 1, 0, 0, false)]
    [InlineData("invalid", 1, 0, 0, false)]
    public void VersionComparisonIsStable(string remote, int major, int minor, int build, bool expected)
    {
        Assert.Equal(expected, centre_app.UpdateService.IsNewer(remote, new Version(major, minor, build)));
    }

    [Fact]
    public void NonShortcutDoesNotResolveAsLink() =>
        Assert.Null(centre_app.ShortcutResolver.Resolve("not-a-shortcut.exe"));

    public void Dispose() { try { File.Delete(_file); } catch { } }
}
