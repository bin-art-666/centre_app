namespace Centre.Tests;

public sealed class LauncherSearchTests
{
    [Fact]
    public void MatchesNamePinyinInitialsAndPathInPriorityOrder()
    {
        var exact = Item("微信", @"C:\Apps\Wechat.exe");
        var path = Item("聊天", @"C:\Apps\weixin-helper.exe");
        var items = new[] { path, exact };

        var nameResults = centre_app.LauncherSearch.FilterAndRank(items, "微信");
        Assert.Same(exact, nameResults[0]);
        Assert.Contains(exact, centre_app.LauncherSearch.FilterAndRank(items, "weixin"));
        Assert.Contains(exact, centre_app.LauncherSearch.FilterAndRank(items, "wx"));
        Assert.Contains(path, centre_app.LauncherSearch.FilterAndRank(items, "helper"));
    }

    [Fact]
    public void EqualScoresPreferRecentAndFrequentItems()
    {
        var old = Item("Visual Studio", "old.exe");
        var recent = Item("Visual Studio Code", "new.exe");
        old.LaunchCount = 100;
        recent.LastLaunchedUtc = DateTimeOffset.UtcNow;
        var results = centre_app.LauncherSearch.FilterAndRank(new[] { old, recent }, "visual");
        Assert.Same(recent, results[0]);
    }

    [Fact]
    public void DisabledPinyinSearchDoesNotLoadDictionaryOrBuildIndex()
    {
        centre_app.PinyinSearchService.Unload();
        var item = new centre_app.LauncherItemData { Name = "微信", TargetPath = "Wechat.exe" };

        centre_app.LauncherSearch.Prepare(item, enablePinyinSearch: false);

        Assert.False(centre_app.PinyinSearchService.IsLoaded);
        Assert.Empty(item.SearchPinyin);
        Assert.Empty(item.SearchInitials);
        Assert.Empty(centre_app.LauncherSearch.FilterAndRank([item], "weixin"));
        Assert.Same(item, centre_app.LauncherSearch.FilterAndRank([item], "微信")[0]);
    }

    private static centre_app.LauncherItemData Item(string name, string path)
    {
        var item = new centre_app.LauncherItemData { Name = name, TargetPath = path };
        centre_app.LauncherSearch.Prepare(item);
        return item;
    }
}
