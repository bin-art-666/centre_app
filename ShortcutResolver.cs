using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.IO;

namespace centre_app;

public sealed record ShortcutInfo(string TargetPath, string WorkingDirectory, string IconPath, int IconIndex);

public static class ShortcutResolver
{
    public static ShortcutInfo? Resolve(string shortcutPath)
    {
        if (!string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(
                new Guid("00021401-0000-0000-C000-000000000046"))!)!;
            ((IPersistFile)link).Load(shortcutPath, 0);
            var target = new StringBuilder(32768);
            var working = new StringBuilder(32768);
            var icon = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            link.GetWorkingDirectory(working, working.Capacity);
            link.GetIconLocation(icon, icon.Capacity, out var iconIndex);
            return new ShortcutInfo(
                Environment.ExpandEnvironmentVariables(target.ToString()),
                Environment.ExpandEnvironmentVariables(working.ToString()),
                Environment.ExpandEnvironmentVariables(icon.ToString()),
                iconIndex);
        }
        catch { return null; }
        finally { if (link is not null) Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr window, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
