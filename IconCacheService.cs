using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.IO;

namespace centre_app;

public static class IconCacheService
{
    public static async Task<BitmapSource?> LoadAsync(LauncherItemData item)
    {
        if (!string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(item.CustomIconPath))
            return LoadBitmap(item.CustomIconPath);

        var fingerprint = await GetFingerprintAsync(item);
        Directory.CreateDirectory(AppDataStore.IconCacheDirectory);
        var cachePath = Path.Combine(AppDataStore.IconCacheDirectory, $"{item.Id:N}-{fingerprint}.png");
        if (File.Exists(cachePath) && LoadBitmap(cachePath) is { } cached) return cached;

        BitmapSource? extracted;
        if (item.TargetKind == LauncherTargetKind.PackagedApp && !string.IsNullOrWhiteSpace(item.AppUserModelId))
            extracted = (await PackagedAppService.FindAsync(item.AppUserModelId, item.PackageFamilyName))?.Icon;
        else
            extracted = await Task.Run(() => ExtractFileIcon(item.TargetPath));

        if (extracted is null) return null;
        SavePng(cachePath, extracted);
        RemoveOldItemEntries(item.Id, cachePath);
        return extracted;
    }

    public static void Invalidate(Guid itemId) => RemoveOldItemEntries(itemId, null);

    public static void Cleanup(IEnumerable<Guid> validIds)
    {
        try
        {
            if (!Directory.Exists(AppDataStore.IconCacheDirectory)) return;
            var valid = validIds.Select(id => id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(AppDataStore.IconCacheDirectory, "*.png"))
            {
                var name = Path.GetFileName(file);
                var separator = name.IndexOf('-');
                if (separator <= 0 || !valid.Contains(name[..separator])) File.Delete(file);
            }
        }
        catch { }
    }

    internal static async Task<string> GetFingerprintAsync(LauncherItemData item)
    {
        string identity;
        if (item.TargetKind == LauncherTargetKind.PackagedApp && !string.IsNullOrWhiteSpace(item.AppUserModelId))
        {
            var app = await PackagedAppService.FindAsync(item.AppUserModelId, item.PackageFamilyName);
            identity = $"uwp|{item.AppUserModelId}|{app?.PackageFullName}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
        }

        var builder = new StringBuilder("file|").Append(Path.GetFullPath(item.TargetPath));
        AppendFileIdentity(builder, item.TargetPath);
        if (ShortcutResolver.Resolve(item.TargetPath) is { } shortcut)
        {
            builder.Append('|').Append(shortcut.TargetPath).Append('|').Append(shortcut.IconPath).Append('|').Append(shortcut.IconIndex);
            AppendFileIdentity(builder, shortcut.TargetPath);
            AppendFileIdentity(builder, shortcut.IconPath);
        }
        identity = builder.ToString();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..20];
    }

    private static void AppendFileIdentity(StringBuilder builder, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var info = new FileInfo(path);
            builder.Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks);
        }
        catch { }
    }

    private static BitmapSource? ExtractFileIcon(string path)
    {
        var shortcut = ShortcutResolver.Resolve(path);
        if (shortcut is { IconPath.Length: > 0 } && File.Exists(shortcut.IconPath) &&
            ExtractIndexedIcon(shortcut.IconPath, shortcut.IconIndex) is { } explicitIcon) return explicitIcon;
        if (shortcut is { TargetPath.Length: > 0 } && File.Exists(shortcut.TargetPath) &&
            GetShellIcon(shortcut.TargetPath) is { } targetIcon) return targetIcon;
        return GetShellIcon(path);
    }

    private static BitmapSource? ExtractIndexedIcon(string path, int index)
    {
        var large = new IntPtr[1];
        if (ExtractIconEx(path, index, large, null, 1) == 0 || large[0] == IntPtr.Zero) return null;
        try { return FromIconHandle(large[0]); }
        finally { DestroyIcon(large[0]); }
    }

    private static BitmapSource? GetShellIcon(string path)
    {
        var info = new ShFileInfo();
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x000000100);
        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero) return null;
        try { return FromIconHandle(info.IconHandle); }
        finally { DestroyIcon(info.IconHandle); }
    }

    private static BitmapSource FromIconHandle(IntPtr handle)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(256, 256));
        source.Freeze();
        return source;
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(path));
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    private static void SavePng(string destination, BitmapSource source)
    {
        var temporary = destination + ".tmp";
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
                stream.Flush(true);
            }
            File.Move(temporary, destination, true);
        }
        catch { try { File.Delete(temporary); } catch { } }
    }

    private static void RemoveOldItemEntries(Guid itemId, string? except)
    {
        try
        {
            if (!Directory.Exists(AppDataStore.IconCacheDirectory)) return;
            foreach (var file in Directory.EnumerateFiles(AppDataStore.IconCacheDirectory, $"{itemId:N}-*.png"))
                if (!string.Equals(file, except, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
        }
        catch { }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, IntPtr[] largeIcons, IntPtr[]? smallIcons, uint iconCount);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
