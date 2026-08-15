namespace centre_app;

public static class LauncherSearch
{
    public static void Prepare(LauncherItemData item, bool enablePinyinSearch = true)
    {
        item.SearchPinyin = string.Empty;
        item.SearchInitials = string.Empty;
        if (!enablePinyinSearch) return;

        if (PinyinSearchService.TryCreateIndex(item.Name, out var pinyin, out var initials))
        {
            item.SearchPinyin = pinyin;
            item.SearchInitials = initials;
        }
    }

    public static List<LauncherItemData> FilterAndRank(
        IReadOnlyList<LauncherItemData> items, string query)
    {
        var normalized = query.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        if (normalized.Length == 0) return [.. items];

        return items.Select((item, index) => new
        {
            Item = item,
            Index = index,
            Score = Score(item, normalized)
        })
            .Where(entry => entry.Score > 0)
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.Item.LastLaunchedUtc)
            .ThenByDescending(entry => entry.Item.LaunchCount)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Item)
            .ToList();
    }

    internal static int Score(LauncherItemData item, string query)
    {
        var name = item.Name.Trim().ToLowerInvariant();
        if (name == query) return 600;
        if (name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return 500;
        if (name.Contains(query, StringComparison.CurrentCultureIgnoreCase)) return 400;
        if (item.SearchPinyin.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            item.SearchInitials.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 300;
        if (item.SearchPinyin.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.SearchInitials.Contains(query, StringComparison.OrdinalIgnoreCase)) return 250;
        if (item.TargetPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (item.AppUserModelId?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)) return 150;
        return 0;
    }
}
