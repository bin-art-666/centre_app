using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using Windows.Storage.Streams;
using System.IO;

namespace centre_app;

public sealed record PackagedAppInfo(
    string DisplayName,
    string AppUserModelId,
    string PackageFamilyName,
    string Publisher,
    string PackageFullName,
    BitmapSource? Icon);

public static class PackagedAppService
{
    public static async Task<IReadOnlyList<PackagedAppInfo>> GetLaunchableAppsAsync()
    {
        var result = new List<PackagedAppInfo>();
        var manager = new PackageManager();
        foreach (var package in manager.FindPackagesForUser(string.Empty))
        {
            try
            {
                var entries = await package.GetAppListEntriesAsync();
                foreach (var entry in entries)
                {
                    var name = entry.DisplayInfo.DisplayName;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(entry.AppUserModelId)) continue;
                    result.Add(new PackagedAppInfo(
                        name, entry.AppUserModelId, package.Id.FamilyName,
                        package.PublisherDisplayName, package.Id.FullName,
                        await LoadLogoAsync(entry.DisplayInfo)));
                }
            }
            catch { }
        }
        return result.OrderBy(app => app.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static async Task<PackagedAppInfo?> FindAsync(string appUserModelId, string? packageFamilyName)
    {
        var manager = new PackageManager();
        var packages = string.IsNullOrWhiteSpace(packageFamilyName)
            ? manager.FindPackagesForUser(string.Empty)
            : manager.FindPackagesForUser(string.Empty, packageFamilyName);
        foreach (var package in packages)
        {
            try
            {
                var entries = await package.GetAppListEntriesAsync();
                var entry = entries.FirstOrDefault(candidate =>
                    string.Equals(candidate.AppUserModelId, appUserModelId, StringComparison.OrdinalIgnoreCase));
                if (entry is null) continue;
                return new PackagedAppInfo(entry.DisplayInfo.DisplayName, entry.AppUserModelId,
                    package.Id.FamilyName, package.PublisherDisplayName, package.Id.FullName,
                    await LoadLogoAsync(entry.DisplayInfo));
            }
            catch { }
        }
        return null;
    }

    public static bool Launch(string appUserModelId)
    {
        try
        {
            var manager = (IApplicationActivationManager)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
            manager.ActivateApplication(appUserModelId, null, 0, out _);
            return true;
        }
        catch { return false; }
    }

    private static async Task<BitmapSource?> LoadLogoAsync(AppDisplayInfo displayInfo)
    {
        try
        {
            var logo = displayInfo.GetLogo(new Windows.Foundation.Size(256, 256));
            using var randomStream = await logo.OpenReadAsync();
            using var input = randomStream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            var length = checked((int)randomStream.Size);
            await reader.LoadAsync((uint)length);
            var bytes = new byte[length];
            reader.ReadBytes(bytes);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
        int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
        int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
    }
}
