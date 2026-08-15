using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace centre_app;

internal static class PinyinSearchService
{
    private static readonly object Gate = new();
    private static PinyinLoadContext? _loadContext;
    private static MethodInfo? _getPinyin;
    private static MethodInfo? _getFirstPinyin;

    internal static bool IsLoaded
    {
        get { lock (Gate) return _loadContext is not null; }
    }

    public static bool TryCreateIndex(string value, out string pinyin, out string initials)
    {
        pinyin = string.Empty;
        initials = string.Empty;
        if (!ContainsChinese(value)) return true;

        lock (Gate)
        {
            try
            {
                EnsureLoaded();
                pinyin = ((string?)_getPinyin!.Invoke(null, [value, false]) ?? string.Empty).ToLowerInvariant();
                initials = ((string?)_getFirstPinyin!.Invoke(null, [value]) ?? string.Empty).ToLowerInvariant();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Unload()
    {
        lock (Gate)
        {
            _getPinyin = null;
            _getFirstPinyin = null;
            var context = _loadContext;
            _loadContext = null;
            context?.Unload();
        }
    }

    private static void EnsureLoaded()
    {
        if (_loadContext is not null) return;

        var assemblyPath = Path.Combine(AppContext.BaseDirectory, "ToolGood.Words.Pinyin.dll");
        var context = new PinyinLoadContext();
        var assembly = context.LoadFromAssemblyPath(assemblyPath);
        var wordsHelper = assembly.GetType("ToolGood.Words.Pinyin.WordsHelper", throwOnError: true)!;
        _getPinyin = wordsHelper.GetMethod("GetPinyin", BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(bool)])
            ?? throw new MissingMethodException(wordsHelper.FullName, "GetPinyin(string, bool)");
        _getFirstPinyin = wordsHelper.GetMethod("GetFirstPinyin", BindingFlags.Public | BindingFlags.Static, [typeof(string)])
            ?? throw new MissingMethodException(wordsHelper.FullName, "GetFirstPinyin(string)");
        _loadContext = context;
    }

    private static bool ContainsChinese(string value)
    {
        foreach (var character in value)
            if (character is >= '\u3400' and <= '\u9FFF' or >= '\uF900' and <= '\uFAFF')
                return true;
        return false;
    }

    private sealed class PinyinLoadContext() : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
