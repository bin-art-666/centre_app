using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace centre_app;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0xCE71;
    private const uint ModNoRepeat = 0x4000;
    private const string InternalDragFormat = "Centre.LauncherItem";
    private const int SpotlightMaxResults = 7;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".appref-ms"
    };

    private readonly List<LauncherItemData> _allItems = [];
    private List<LauncherItemData> _visibleItems = [];
    private readonly SemaphoreSlim _iconLoader = new(4);
    private readonly Dictionary<Guid, Task<BitmapSource?>> _iconLoadTasks = [];
    private readonly DispatcherTimer _toastTimer;
    private readonly DispatcherTimer _edgePageTimer;
    private LauncherSettings _settings;
    private LauncherSettings? _settingsSnapshot;
    private int _currentPage;
    private HwndSource? _source;
    private System.Windows.Point _dragStart;
    private System.Windows.Point _backgroundPressPoint;
    private LauncherItemData? _pressedItem;
    private bool _backgroundClickCandidate;
    private bool _suppressNextClick;
    private bool _isLoaded;
    private bool _isSettingsInitializing;
    private bool _syncingSearch;
    private bool _spotlightDark = true;
    private int _spotlightSelectedIndex;
    private int _pendingDropIndex = -1;
    private int _edgePageDirection;
    private uint _pendingHotkeyModifiers;
    private int _pendingHotkeyVirtualKey;
    private uint _registeredHotkeyModifiers;
    private int _registeredHotkeyVirtualKey;
    private UpdateInfo? _availableUpdate;
    private MonitorBounds _activeMonitor;
    private DragPreviewPopup? _dragPreviewPopup;
    private bool _isMinimizedToTaskbar;
    private double _capturedBackgroundBlur = double.NaN;
    private bool _capturedStaticBlackBackground;

    private int ItemsPerPage => _settings.Rows * _settings.Columns;

    public MainWindow()
    {
        _activeMonitor = GetCursorMonitor();
        var workArea = _activeMonitor.WorkArea;
        _settings = AppDataStore.LoadSettings();
        _settings.Normalize(Math.Max(800, workArea.Width), Math.Max(600, workArea.Height));
        var desktop = _settings.StaticBlackBackground
            ? null
            : CaptureDesktop(_activeMonitor.PixelBounds, _settings.BackgroundBlur);
        InitializeComponent();
        DesktopBackground.Source = desktop;
        _capturedBackgroundBlur = _settings.BackgroundBlur;
        _capturedStaticBlackBackground = _settings.StaticBlackBackground;
        ApplyWindowSettings(_settings);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastBorder.Visibility = Visibility.Collapsed; };
        _edgePageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _edgePageTimer.Tick += (_, _) =>
        {
            _edgePageTimer.Stop();
            if (_edgePageDirection != 0) ChangePage(_edgePageDirection);
        };

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _allItems.AddRange(AppDataStore.LoadItems().Where(item =>
            item is not null && item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Name) &&
            (item.TargetKind == LauncherTargetKind.PackagedApp
                ? !string.IsNullOrWhiteSpace(item.AppUserModelId)
                : !string.IsNullOrWhiteSpace(item.TargetPath))));
        foreach (var item in _allItems) LauncherSearch.Prepare(item, _settings.EnablePinyinSearch);
        IconCacheService.Cleanup(_allItems.Select(item => item.Id));
        _visibleItems = [.. _allItems];
        _isLoaded = true;
        ApplyGridSettings();
        ApplyDisplayMode();
        RenderCurrentView();
        AnimateIn();
        FocusActiveSearch();
        LauncherFooterHint.Text = $"Esc 最小化到任务栏  ·  {FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey)} 随时呼出";
        _ = CheckUpdatesOnStartupAsync();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        if (!RegisterHotKey(handle, HotkeyId, _settings.HotkeyModifiers | ModNoRepeat, (uint)_settings.HotkeyVirtualKey))
            ShowToast($"{FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey)} 已被其他程序占用");
        else
        {
            _registeredHotkeyModifiers = _settings.HotkeyModifiers;
            _registeredHotkeyVirtualKey = _settings.HotkeyVirtualKey;
        }
        ApplyDwmCorners(!_settings.FullScreen && !_settings.FloatingSearchMode);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, HotkeyId);
        _source?.RemoveHook(WndProc);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _isMinimizedToTaskbar = true;
            Topmost = false;
            return;
        }

        if (!_isMinimizedToTaskbar || WindowState != WindowState.Normal) return;
        _isMinimizedToTaskbar = false;
        Dispatcher.BeginInvoke(() =>
        {
            _activeMonitor = GetCursorMonitor();
            RefreshDesktopBackground();
            ApplyWindowSettings(_settings);
            ApplyDisplayMode();
            Topmost = true;
            Activate();
            AnimateIn();
            FocusActiveSearch();
        });
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            if (IsLauncherOpen) HideLauncher(); else ShowLauncher();
            handled = true;
        }
        else if (msg == 0x02E0 || msg == 0x007E)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _activeMonitor = GetCursorMonitor();
                ApplyWindowSettings(_settings);
                RefreshDesktopBackground();
            });
        }
        return IntPtr.Zero;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppsDialog(this);
        if (dialog.ShowDialog() != true) return;
        if (dialog.LocalPaths.Count > 0) AddFiles(dialog.LocalPaths);
        if (dialog.SelectedPackagedApps.Count > 0) AddPackagedApps(dialog.SelectedPackagedApps);
    }

    private void AddPackagedApps(IEnumerable<PackagedAppInfo> apps)
    {
        var added = 0;
        var duplicates = 0;
        foreach (var app in apps)
        {
            if (_allItems.Any(item => string.Equals(item.AppUserModelId, app.AppUserModelId, StringComparison.OrdinalIgnoreCase)))
            {
                duplicates++;
                continue;
            }
            var item = new LauncherItemData
            {
                Id = Guid.NewGuid(),
                Name = app.DisplayName,
                TargetKind = LauncherTargetKind.PackagedApp,
                AppUserModelId = app.AppUserModelId,
                PackageFamilyName = app.PackageFamilyName
            };
            LauncherSearch.Prepare(item, _settings.EnablePinyinSearch);
            _allItems.Add(item);
            added++;
        }
        if (added > 0)
        {
            PersistItems();
            RefreshFilter();
            _currentPage = Math.Max(0, (_visibleItems.Count - 1) / ItemsPerPage);
            RenderCurrentView(true);
        }
        ShowToast(duplicates > 0 ? $"已添加 {added} 个应用，跳过 {duplicates} 个重复项" : $"已添加 {added} 个应用");
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var duplicates = 0;
        var invalid = 0;

        foreach (var rawPath in paths)
        {
            string path;
            try { path = Path.GetFullPath(rawPath); }
            catch { invalid++; continue; }

            if (!File.Exists(path) || !SupportedExtensions.Contains(Path.GetExtension(path)))
            {
                invalid++;
                continue;
            }

            if (_allItems.Any(item => string.Equals(NormalizePath(item.TargetPath), path, StringComparison.OrdinalIgnoreCase)))
            {
                duplicates++;
                continue;
            }

            var item = new LauncherItemData
            {
                Id = Guid.NewGuid(),
                Name = GetDefaultName(path),
                TargetPath = path
            };
            LauncherSearch.Prepare(item, _settings.EnablePinyinSearch);
            _allItems.Add(item);
            added++;
        }

        if (added > 0)
        {
            PersistItems();
            RefreshFilter();
            _currentPage = Math.Max(0, (_visibleItems.Count - 1) / ItemsPerPage);
            RenderCurrentView(true);
        }

        var parts = new List<string>();
        if (added > 0) parts.Add($"已添加 {added} 个应用");
        if (duplicates > 0) parts.Add($"跳过 {duplicates} 个重复项");
        if (invalid > 0) parts.Add($"忽略 {invalid} 个不支持的文件");
        ShowToast(parts.Count > 0 ? string.Join("，", parts) : "没有可添加的应用");
    }

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static string GetDefaultName(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".appref-ms".Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private void Window_DragEnter(object sender, System.Windows.DragEventArgs e) => SetDragEffect(e);
    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        SetDragEffect(e);
        if (e.Data.GetDataPresent(InternalDragFormat)) _dragPreviewPopup?.Update(e.GetPosition(PageHost));
    }

    private static void SetDragEffect(System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDragFormat)) e.Effects = System.Windows.DragDropEffects.Move;
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) e.Effects = System.Windows.DragDropEffects.Copy;
        else e.Effects = System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(InternalDragFormat)) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files) AddFiles(files);
        e.Handled = true;
    }

    private void RenderPage(bool animate = false, int direction = 1)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_visibleItems.Count / (double)ItemsPerPage));
        _currentPage = Math.Clamp(_currentPage, 0, pageCount - 1);
        AppGrid.Children.Clear();

        var hasQuery = !string.IsNullOrWhiteSpace(SearchBox.Text);
        EmptyPanel.Visibility = _allItems.Count == 0 && !hasQuery ? Visibility.Visible : Visibility.Collapsed;
        SearchEmptyText.Visibility = _visibleItems.Count == 0 && (_allItems.Count > 0 || hasQuery) ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in _visibleItems.Skip(_currentPage * ItemsPerPage).Take(ItemsPerPage))
            AppGrid.Children.Add(CreateLauncherButton(item));

        PageIndicators.Children.Clear();
        if (pageCount > 1)
        {
            for (var i = 0; i < pageCount; i++)
            {
                var page = i;
                var dot = new Button
                {
                    Width = i == _currentPage ? 18 : 8,
                    Height = 8,
                    Margin = new Thickness(4),
                    Background = new SolidColorBrush(i == _currentPage
                        ? System.Windows.Media.Color.FromArgb(235, 255, 255, 255)
                        : System.Windows.Media.Color.FromArgb(100, 255, 255, 255)),
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    AllowDrop = true,
                    Template = RoundedButtonTemplate()
                };
                dot.Click += (_, _) =>
                {
                    var old = _currentPage;
                    _currentPage = page;
                    RenderPage(true, page >= old ? 1 : -1);
                };
                dot.DragEnter += (_, args) =>
                {
                    if (!args.Data.GetDataPresent(InternalDragFormat) || _currentPage == page) return;
                    _currentPage = page;
                    RenderPage(true, 1);
                };
                PageIndicators.Children.Add(dot);
            }
        }

        if (animate) AnimatePage(direction);
    }

    private void RenderCurrentView(bool animate = false, int direction = 1)
    {
        if (_settings.FloatingSearchMode) RenderSpotlight(animate);
        else RenderPage(animate, direction);
    }

    private void RenderSpotlight(bool animate = false)
    {
        if (!_isLoaded) return;
        var query = SpotlightSearchBox.Text.Trim();
        var expanded = query.Length > 0;
        var results = expanded
            ? _visibleItems.Take(SpotlightMaxResults).ToList()
            : _allItems.Where(item => item.LastLaunchedUtc.HasValue)
                .OrderByDescending(item => item.LastLaunchedUtc)
                .Take(5).ToList();
        _spotlightSelectedIndex = results.Count == 0 ? 0 : Math.Clamp(_spotlightSelectedIndex, 0, results.Count - 1);
        SpotlightResults.Children.Clear();

        if (results.Count == 0)
        {
            SpotlightResults.Children.Add(new TextBlock
            {
                Text = expanded ? "没有找到匹配的应用" : "启动应用后，这里会显示最近使用",
                Foreground = BrushFrom(_spotlightDark ? "#98FFFFFF" : "#78000000"),
                FontSize = 14,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 18)
            });
        }
        else
        {
            for (var index = 0; index < results.Count; index++)
                SpotlightResults.Children.Add(CreateSpotlightResult(results[index], index));
        }

        SpotlightResultsBorder.Visibility = expanded || _allItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SpotlightSubtitle.Text = _allItems.Count == 0
            ? "点击 + 添加你的第一个应用"
            : expanded
                ? $"找到 {_visibleItems.Count} 个结果"
                : "最近使用";

        var resultHeight = results.Count == 0 ? 78 : results.Count * 72 + 18;
        var showResults = expanded || _allItems.Count > 0;
        var targetHeight = showResults ? Math.Min(730, 190 + resultHeight) : 190;
        if (animate)
        {
            SpotlightCard.BeginAnimation(HeightProperty,
                new DoubleAnimation(SpotlightCard.ActualHeight > 0 ? SpotlightCard.ActualHeight : SpotlightCard.Height, targetHeight,
                    TimeSpan.FromMilliseconds(260))
                { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
        }
        else
        {
            SpotlightCard.BeginAnimation(HeightProperty, null);
            SpotlightCard.Height = targetHeight;
        }
    }

    private Button CreateSpotlightResult(LauncherItemData item, int index)
    {
        const double iconSize = 48;
        var palette = new[] { "#4E78E8", "#7A5CE6", "#E45B72", "#2AAE91", "#E88B3D", "#3E9CCC" };
        var fallback = new Border
        {
            Width = iconSize,
            Height = iconSize,
            CornerRadius = new CornerRadius(4),
            Background = BrushFrom(palette[(item.Name.GetHashCode() & int.MaxValue) % palette.Length]),
            Child = new TextBlock
            {
                Text = GetInitial(item.Name),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        var image = new System.Windows.Controls.Image
        {
            Source = item.Icon,
            Opacity = item.Icon is null ? 0 : 1,
            Width = iconSize,
            Height = iconSize,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        fallback.Visibility = item.Icon is null ? Visibility.Visible : Visibility.Collapsed;
        var iconHost = new Grid { Width = iconSize, Height = iconSize };
        iconHost.Children.Add(fallback);
        iconHost.Children.Add(image);
        _ = LoadIconAsync(item, image, fallback);

        var title = new TextBlock
        {
            Text = item.Name,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFrom(_spotlightDark ? "#F5FFFFFF" : "#E5000000"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var subtitle = new TextBlock
        {
            Text = item.TargetKind == LauncherTargetKind.PackagedApp ? "Microsoft Store 应用" : item.TargetPath,
            FontSize = 11.5,
            Foreground = BrushFrom(_spotlightDark ? "#86FFFFFF" : "#70000000"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0)
        };
        var text = new StackPanel { Margin = new Thickness(14, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(subtitle);

        var openHint = new TextBlock
        {
            Text = index == _spotlightSelectedIndex ? "↵" : string.Empty,
            FontSize = 15,
            Foreground = BrushFrom(_spotlightDark ? "#A8FFFFFF" : "#76000000"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0)
        };
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(iconHost, 0);
        Grid.SetColumn(text, 1);
        Grid.SetColumn(openHint, 2);
        content.Children.Add(iconHost);
        content.Children.Add(text);
        content.Children.Add(openHint);

        var button = new Button
        {
            Content = content,
            Tag = item,
            Height = 68,
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(12, 8, 8, 8),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = index == _spotlightSelectedIndex
                ? BrushFrom(_spotlightDark ? "#2FFFFFFF" : "#16000000")
                : System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Template = ResultButtonTemplate(),
            ContextMenu = CreateItemContextMenu(item)
        };
        button.Click += (_, _) => LaunchItem(item);
        button.MouseEnter += (_, _) => { _spotlightSelectedIndex = index; UpdateSpotlightSelection(); };
        return button;
    }

    private void UpdateSpotlightSelection()
    {
        for (var index = 0; index < SpotlightResults.Children.Count; index++)
        {
            if (SpotlightResults.Children[index] is not Button button) continue;
            button.Background = index == _spotlightSelectedIndex
                ? BrushFrom(_spotlightDark ? "#2FFFFFFF" : "#16000000")
                : System.Windows.Media.Brushes.Transparent;
            if (button.Content is Grid grid && grid.Children.OfType<TextBlock>().FirstOrDefault() is { } hint)
                hint.Text = index == _spotlightSelectedIndex ? "↵" : string.Empty;
        }
    }

    private static ControlTemplate ResultButtonTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(18));
        factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = factory };
    }

    private static SolidColorBrush BrushFrom(string value) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;

    private Button CreateLauncherButton(LauncherItemData item)
    {
        var palette = new[] { "#4E78E8", "#7A5CE6", "#E45B72", "#2AAE91", "#E88B3D", "#3E9CCC" };
        var fallback = new Border
        {
            Width = _settings.IconSize,
            Height = _settings.IconSize,
            CornerRadius = new CornerRadius(5),
            Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFromString(
                palette[(item.Name.GetHashCode() & int.MaxValue) % palette.Length])!,
            Child = new TextBlock
            {
                Text = GetInitial(item.Name),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = _settings.IconSize * .42,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var image = new System.Windows.Controls.Image
        {
            Source = item.Icon,
            Opacity = item.Icon is null ? 0 : 1,
            Width = _settings.IconSize,
            Height = _settings.IconSize,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        fallback.Visibility = item.Icon is null ? Visibility.Visible : Visibility.Collapsed;

        var iconHost = new Grid { Width = _settings.IconSize, Height = _settings.IconSize };
        iconHost.Children.Add(fallback);
        iconHost.Children.Add(image);
        _ = LoadIconAsync(item, image, fallback);

        var label = new TextBlock
        {
            Text = item.Name,
            Foreground = System.Windows.Media.Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text,Segoe UI"),
            FontSize = 14,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Math.Max(110, _settings.IconSize + 54),
            Margin = new Thickness(0, 8, 0, 0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = .8, Color = Colors.Black }
        };

        var panel = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
        panel.Children.Add(iconHost);
        panel.Children.Add(label);

        var button = new Button
        {
            Content = panel,
            Tag = item,
            Style = (Style)FindResource("LauncherButtonStyle"),
            Margin = new Thickness(5, 2, 5, 2),
            ToolTip = item.Name,
            AllowDrop = true,
            ContextMenu = CreateItemContextMenu(item)
        };
        button.Click += LauncherButton_Click;
        button.PreviewMouseLeftButtonDown += LauncherButton_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += LauncherButton_PreviewMouseMove;
        button.DragOver += LauncherButton_DragOver;
        button.Drop += LauncherButton_Drop;
        return button;
    }

    private static string GetInitial(string name)
    {
        var trimmed = name.Trim();
        return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
    }

    private async Task LoadIconAsync(LauncherItemData item, System.Windows.Controls.Image image, Border fallback)
    {
        if (item.Icon is null)
        {
            if (!_iconLoadTasks.TryGetValue(item.Id, out var loadTask))
            {
                loadTask = LoadIconWithThrottleAsync(item);
                _iconLoadTasks[item.Id] = loadTask;
            }

            try
            {
                item.Icon ??= await loadTask;
            }
            finally
            {
                if (_iconLoadTasks.TryGetValue(item.Id, out var currentTask) && ReferenceEquals(currentTask, loadTask))
                    _iconLoadTasks.Remove(item.Id);
            }
        }

        image.Source = item.Icon;
        fallback.Visibility = item.Icon is null ? Visibility.Visible : Visibility.Collapsed;
        if (item.Icon is not null && image.Opacity < 1)
            image.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    private async Task<BitmapSource?> LoadIconWithThrottleAsync(LauncherItemData item)
    {
        await _iconLoader.WaitAsync();
        try
        {
            return await IconCacheService.LoadAsync(item);
        }
        catch
        {
            return null;
        }
        finally
        {
            _iconLoader.Release();
        }
    }

    private ContextMenu CreateItemContextMenu(LauncherItemData item)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "重命名" };
        rename.Click += (_, _) => RenameItem(item);
        var changeIcon = new MenuItem { Header = "更换图标…" };
        changeIcon.Click += (_, _) => ChangeItemIcon(item);
        var resetIcon = new MenuItem { Header = "恢复默认图标", IsEnabled = !string.IsNullOrWhiteSpace(item.CustomIconPath) };
        resetIcon.Click += (_, _) => ResetItemIcon(item);
        var remove = new MenuItem { Header = "删除" };
        remove.Click += (_, _) => RemoveItem(item);
        menu.Items.Add(rename);
        menu.Items.Add(changeIcon);
        menu.Items.Add(resetIcon);
        menu.Items.Add(new Separator());
        menu.Items.Add(remove);
        return menu;
    }

    private void RenameItem(LauncherItemData item)
    {
        var dialog = new RenameDialog(this, item.Name);
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Result)) return;
        item.Name = dialog.Result;
        LauncherSearch.Prepare(item, _settings.EnablePinyinSearch);
        PersistItems();
        RefreshFilter();
        RenderCurrentView(true);
    }

    private void ChangeItemIcon(LauncherItemData item)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择应用图标",
            Filter = "图片和图标 (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            item.CustomIconPath = AppDataStore.CopyCustomIcon(item.Id, dialog.FileName);
            IconCacheService.Invalidate(item.Id);
            item.Icon = null;
            PersistItems();
            RenderCurrentView(true);
            ShowToast("图标已更新");
        }
        catch (Exception ex) { ShowToast($"无法更换图标：{ex.Message}"); }
    }

    private void ResetItemIcon(LauncherItemData item)
    {
        AppDataStore.DeleteCustomIcon(item);
        IconCacheService.Invalidate(item.Id);
        item.CustomIconPath = null;
        item.Icon = null;
        PersistItems();
        RenderCurrentView(true);
        ShowToast("已恢复默认图标");
    }

    private void RemoveItem(LauncherItemData item)
    {
        if (System.Windows.MessageBox.Show(this, $"从应用中心中删除“{item.Name}”？\n原始程序不会被删除。", "删除应用",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        AppDataStore.DeleteCustomIcon(item);
        IconCacheService.Invalidate(item.Id);
        _allItems.Remove(item);
        PersistItems();
        RefreshFilter();
        RenderCurrentView(true, -1);
        ShowToast("应用已移除");
    }

    private void LauncherButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _pressedItem = (sender as Button)?.Tag as LauncherItemData;
    }

    private void LauncherButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedItem is null) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _suppressNextClick = true;
        var data = new System.Windows.DataObject(InternalDragFormat, _pressedItem.Id.ToString("D"));
        var draggedButton = sender as Button;
        if (draggedButton is not null)
        {
            _dragPreviewPopup = new DragPreviewPopup(PageHost, draggedButton);
            _dragPreviewPopup.Show(e.GetPosition(PageHost));
            draggedButton.Opacity = .28;
        }
        try { System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, data, System.Windows.DragDropEffects.Move); }
        finally
        {
            draggedButton?.SetCurrentValue(OpacityProperty, 1d);
            _dragPreviewPopup?.Dispose();
            _dragPreviewPopup = null;
            DropIndicator.Visibility = Visibility.Collapsed;
            _edgePageTimer.Stop();
            _edgePageDirection = 0;
        }
        _pressedItem = null;
        var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        resetTimer.Tick += (_, _) => { resetTimer.Stop(); _suppressNextClick = false; };
        resetTimer.Start();
    }

    private void LauncherButton_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalDragFormat)) return;
        e.Effects = System.Windows.DragDropEffects.Move;
        UpdateDragVisuals(e);
        e.Handled = true;
    }

    private void LauncherButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDraggedItem(e, out var source) || (sender as Button)?.Tag is not LauncherItemData target || ReferenceEquals(source, target)) return;
        var targetButton = (Button)sender;
        var insertAfter = e.GetPosition(targetButton).X >= targetButton.ActualWidth / 2;
        _allItems.Remove(source);
        var targetIndex = _allItems.IndexOf(target);
        _allItems.Insert(Math.Clamp(targetIndex + (insertAfter ? 1 : 0), 0, _allItems.Count), source);
        FinishReorder(e);
    }

    private void AppGrid_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalDragFormat)) return;
        e.Effects = System.Windows.DragDropEffects.Move;
        UpdateDragVisuals(e);
        e.Handled = true;
    }

    private void UpdateDragVisuals(System.Windows.DragEventArgs e)
    {
        _dragPreviewPopup?.Update(e.GetPosition(PageHost));
        var position = e.GetPosition(AppGrid);
        UpdateDropIndicator(position);
        var direction = position.X < 48 ? -1 : position.X > AppGrid.ActualWidth - 48 ? 1 : 0;
        if (direction != _edgePageDirection)
        {
            _edgePageTimer.Stop();
            _edgePageDirection = direction;
            if (direction != 0) _edgePageTimer.Start();
        }
    }

    private void AppGrid_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDraggedItem(e, out var source)) return;
        var insertionIndex = _pendingDropIndex >= 0
            ? Math.Clamp(_pendingDropIndex, 0, _allItems.Count)
            : Math.Min((_currentPage + 1) * ItemsPerPage, _allItems.Count);
        var oldIndex = _allItems.IndexOf(source);
        _allItems.Remove(source);
        if (oldIndex < insertionIndex) insertionIndex--;
        _allItems.Insert(Math.Clamp(insertionIndex, 0, _allItems.Count), source);
        FinishReorder(e);
    }

    private void UpdateDropIndicator(System.Windows.Point position)
    {
        if (AppGrid.ActualWidth <= 0 || AppGrid.ActualHeight <= 0) return;
        var cellWidth = AppGrid.ActualWidth / Math.Max(1, _settings.Columns);
        var cellHeight = AppGrid.ActualHeight / Math.Max(1, _settings.Rows);
        var column = Math.Clamp((int)(position.X / cellWidth), 0, _settings.Columns - 1);
        var row = Math.Clamp((int)(position.Y / cellHeight), 0, _settings.Rows - 1);
        var after = position.X - column * cellWidth >= cellWidth / 2;
        var localIndex = Math.Min(row * _settings.Columns + column, Math.Max(0, _visibleItems.Count - _currentPage * ItemsPerPage));
        var visibleTargetIndex = Math.Min(_currentPage * ItemsPerPage + localIndex, _visibleItems.Count);
        if (visibleTargetIndex < _visibleItems.Count)
        {
            var targetIndex = _allItems.IndexOf(_visibleItems[visibleTargetIndex]);
            _pendingDropIndex = Math.Max(0, targetIndex + (after ? 1 : 0));
        }
        else _pendingDropIndex = _allItems.Count;

        DropIndicator.Height = Math.Max(48, cellHeight * .72);
        Canvas.SetLeft(DropIndicator, Math.Clamp((column + (after ? 1 : 0)) * cellWidth - 2, 0, Math.Max(0, AppGrid.ActualWidth - 4)));
        Canvas.SetTop(DropIndicator, row * cellHeight + (cellHeight - DropIndicator.Height) / 2);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private bool TryGetDraggedItem(System.Windows.DragEventArgs e, out LauncherItemData item)
    {
        item = null!;
        if (e.Data.GetData(InternalDragFormat) is not string idText || !Guid.TryParse(idText, out var id)) return false;
        item = _allItems.FirstOrDefault(candidate => candidate.Id == id)!;
        return item is not null;
    }

    private void FinishReorder(System.Windows.DragEventArgs e)
    {
        PersistItems();
        RefreshFilter();
        RenderCurrentView(true);
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
        _pendingDropIndex = -1;
        DropIndicator.Visibility = Visibility.Collapsed;
    }

    private void LauncherButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressNextClick) { _suppressNextClick = false; return; }
        if (sender is not Button { Tag: LauncherItemData item }) return;
        LaunchItem(item);
    }

    private void LaunchItem(LauncherItemData item)
    {
        if (item.TargetKind == LauncherTargetKind.File && !File.Exists(item.TargetPath))
        {
            ShowToast($"找不到“{item.Name}”的目标文件");
            return;
        }
        try
        {
            var launched = item.TargetKind == LauncherTargetKind.PackagedApp
                ? !string.IsNullOrWhiteSpace(item.AppUserModelId) && PackagedAppService.Launch(item.AppUserModelId)
                : Process.Start(new ProcessStartInfo(item.TargetPath) { UseShellExecute = true }) is not null;
            if (!launched)
            {
                ShowToast($"无法启动 {item.Name}，应用可能已卸载");
                return;
            }
            item.LaunchCount++;
            item.LastLaunchedUtc = DateTimeOffset.UtcNow;
            PersistItems();
            HideLauncher();
        }
        catch (Exception ex) { ShowToast($"无法启动 {item.Name}：{ex.Message}"); }
    }

    private static ControlTemplate RoundedButtonTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        return new ControlTemplate(typeof(Button)) { VisualTree = factory };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearch) return;
        _syncingSearch = true;
        SpotlightSearchBox.Text = SearchBox.Text;
        SpotlightSearchBox.CaretIndex = SpotlightSearchBox.Text.Length;
        _syncingSearch = false;
        HandleSearchChanged(SearchBox.Text);
    }

    private void SpotlightSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingSearch) return;
        _syncingSearch = true;
        SearchBox.Text = SpotlightSearchBox.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        _syncingSearch = false;
        HandleSearchChanged(SpotlightSearchBox.Text);
    }

    private void HandleSearchChanged(string text)
    {
        if (!_isLoaded) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        SpotlightSearchHint.Visibility = string.IsNullOrEmpty(SpotlightSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _currentPage = 0;
        _spotlightSelectedIndex = 0;
        RefreshFilter();
        RenderCurrentView(true);
    }

    private void RefreshFilter()
    {
        var query = (_settings.FloatingSearchMode ? SpotlightSearchBox.Text : SearchBox.Text).Trim();
        _visibleItems = LauncherSearch.FilterAndRank(_allItems, query);
    }

    private void SetPinyinSearchEnabled(bool enabled)
    {
        foreach (var item in _allItems) LauncherSearch.Prepare(item, enabled);
        if (!enabled)
        {
            PinyinSearchService.Unload();
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            });
        }
        RefreshFilter();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.FloatingSearchMode) SpotlightSearchBox.Clear(); else SearchBox.Clear();
        FocusActiveSearch();
    }

    private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftAlt or Key.RightAlt)
        {
            SetPendingHotkey(0, 0x12);
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        uint nativeModifiers = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) nativeModifiers |= 0x0001;
        if (modifiers.HasFlag(ModifierKeys.Control)) nativeModifiers |= 0x0002;
        if (modifiers.HasFlag(ModifierKeys.Shift)) nativeModifiers |= 0x0004;
        if (modifiers.HasFlag(ModifierKeys.Windows)) nativeModifiers |= 0x0008;
        if (nativeModifiers == 0 || key is Key.None or Key.Escape)
        {
            ShowToast("快捷键必须包含 Ctrl、Alt、Shift 或 Win");
            return;
        }
        _pendingHotkeyModifiers = nativeModifiers;
        _pendingHotkeyVirtualKey = KeyInterop.VirtualKeyFromKey(key);
        HotkeyTextBox.Text = FormatHotkey(_pendingHotkeyModifiers, _pendingHotkeyVirtualKey);
    }

    private void ResetHotkey_Click(object sender, RoutedEventArgs e) => SetPendingHotkey(0, 0x12);
    private void AltPreset_Click(object sender, RoutedEventArgs e) => SetPendingHotkey(0, 0x12);
    private void ShiftTabPreset_Click(object sender, RoutedEventArgs e) => SetPendingHotkey(0x0004, 0x09);
    private void AltSpacePreset_Click(object sender, RoutedEventArgs e) => SetPendingHotkey(0x0001, 0x20);

    private void SetPendingHotkey(uint modifiers, int virtualKey)
    {
        _pendingHotkeyModifiers = modifiers;
        _pendingHotkeyVirtualKey = virtualKey;
        HotkeyTextBox.Text = FormatHotkey(modifiers, virtualKey);
    }

    private bool TryApplyHotkey(uint modifiers, int virtualKey)
    {
        if (modifiers == _registeredHotkeyModifiers && virtualKey == _registeredHotkeyVirtualKey) return true;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return false;
        UnregisterHotKey(handle, HotkeyId);
        if (RegisterHotKey(handle, HotkeyId, modifiers | ModNoRepeat, (uint)virtualKey))
        {
            _registeredHotkeyModifiers = modifiers;
            _registeredHotkeyVirtualKey = virtualKey;
            return true;
        }
        RegisterHotKey(handle, HotkeyId, _registeredHotkeyModifiers | ModNoRepeat, (uint)_registeredHotkeyVirtualKey);
        return false;
    }

    private static string FormatHotkey(uint modifiers, int virtualKey)
    {
        if (modifiers == 0 && virtualKey == 0x12) return "Alt";
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        parts.Add(KeyInterop.KeyFromVirtualKey(virtualKey).ToString());
        return string.Join(" + ", parts);
    }

    private async Task CheckUpdatesOnStartupAsync()
    {
        if (!_settings.AutoCheckUpdates ||
            (_settings.LastUpdateCheckUtc.HasValue && DateTimeOffset.UtcNow - _settings.LastUpdateCheckUtc.Value < TimeSpan.FromDays(1))) return;
        await CheckUpdatesAsync(false);
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);

    private async Task CheckUpdatesAsync(bool showCurrentMessage)
    {
        try
        {
            var update = await UpdateService.CheckAsync();
            _settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow;
            if (_settingsSnapshot is not null) _settingsSnapshot.LastUpdateCheckUtc = _settings.LastUpdateCheckUtc;
            PersistUpdateMetadata();
            if (update is null)
            {
                if (showCurrentMessage) ShowToast("当前已是最新版本");
                return;
            }
            if (string.Equals(_settings.DismissedUpdateVersion, update.Version, StringComparison.OrdinalIgnoreCase)) return;
            _availableUpdate = update;
            UpdateBannerText.Text = $"发现新版本 {update.Version}";
            UpdateBanner.Visibility = Visibility.Visible;
        }
        catch
        {
            if (showCurrentMessage) ShowToast("暂时无法检查更新");
        }
    }

    private void ViewUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null) return;
        Process.Start(new ProcessStartInfo(_availableUpdate.ReleaseUrl) { UseShellExecute = true });
    }

    private void DismissUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
        {
            _settings.DismissedUpdateVersion = _availableUpdate.Version;
            if (_settingsSnapshot is not null) _settingsSnapshot.DismissedUpdateVersion = _availableUpdate.Version;
            PersistUpdateMetadata();
        }
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private void PersistUpdateMetadata()
    {
        try { AppDataStore.SaveSettings(_settingsSnapshot ?? _settings); } catch { }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsLayer.Visibility == Visibility.Visible) return;
        _settingsSnapshot = _settings.Clone();
        var workArea = _activeMonitor.WorkArea;
        _isSettingsInitializing = true;
        WidthSlider.Maximum = Math.Max(800, workArea.Width);
        HeightSlider.Maximum = Math.Max(600, workArea.Height);
        AppAreaWidthSlider.Maximum = Math.Max(600, workArea.Width - 92);
        AppAreaHeightSlider.Maximum = Math.Max(420, workArea.Height - 58);
        FloatingSearchModeCheck.IsChecked = _settings.FloatingSearchMode;
        FullScreenCheck.IsChecked = _settings.FullScreen;
        WidthSlider.Value = Math.Clamp(_settings.WindowWidth, WidthSlider.Minimum, WidthSlider.Maximum);
        HeightSlider.Value = Math.Clamp(_settings.WindowHeight, HeightSlider.Minimum, HeightSlider.Maximum);
        AppAreaWidthSlider.Value = Math.Clamp(_settings.AppAreaWidth, AppAreaWidthSlider.Minimum, AppAreaWidthSlider.Maximum);
        AppAreaHeightSlider.Value = Math.Clamp(_settings.AppAreaHeight, AppAreaHeightSlider.Minimum, AppAreaHeightSlider.Maximum);
        ColumnsSlider.Value = _settings.Columns;
        RowsSlider.Value = _settings.Rows;
        IconSizeSlider.Value = _settings.IconSize;
        PinyinSearchCheck.IsChecked = _settings.EnablePinyinSearch;
        StaticBlackBackgroundCheck.IsChecked = _settings.StaticBlackBackground;
        BackgroundBlurSlider.Value = _settings.BackgroundBlur;
        _pendingHotkeyModifiers = _settings.HotkeyModifiers;
        _pendingHotkeyVirtualKey = _settings.HotkeyVirtualKey;
        HotkeyTextBox.Text = FormatHotkey(_pendingHotkeyModifiers, _pendingHotkeyVirtualKey);
        AutoUpdateCheck.IsChecked = _settings.AutoCheckUpdates;
        _isSettingsInitializing = false;
        UpdateSettingsLabels();

        SettingsLayer.Visibility = Visibility.Visible;
        SettingsTranslate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(430, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SettingsControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_isSettingsInitializing || SettingsLayer.Visibility != Visibility.Visible) return;
        UpdateSettingsLabels();
        var wasPinyinEnabled = _settings.EnablePinyinSearch;
        _settings = ReadSettingsControls();
        if (wasPinyinEnabled != _settings.EnablePinyinSearch)
            SetPinyinSearchEnabled(_settings.EnablePinyinSearch);
        ApplyWindowSettings(_settings);
        ApplyGridSettings();
        ApplyDisplayMode();
        RenderCurrentView(true);
    }

    private void UpdateSettingsLabels()
    {
        var floating = FloatingSearchModeCheck.IsChecked == true;
        FullScreenCheck.IsEnabled = !floating;
        WindowSizePanel.IsEnabled = !floating && FullScreenCheck.IsChecked != true;
        AppAreaSizePanel.IsEnabled = !floating && FullScreenCheck.IsChecked == true;
        BackgroundBlurPanel.IsEnabled = StaticBlackBackgroundCheck.IsChecked != true;
        WidthValueText.Text = $"{Math.Round(WidthSlider.Value):0} px";
        HeightValueText.Text = $"{Math.Round(HeightSlider.Value):0} px";
        AppAreaWidthValueText.Text = $"{Math.Round(AppAreaWidthSlider.Value):0} px";
        AppAreaHeightValueText.Text = $"{Math.Round(AppAreaHeightSlider.Value):0} px";
        ColumnsValueText.Text = $"{Math.Round(ColumnsSlider.Value):0} 列";
        RowsValueText.Text = $"{Math.Round(RowsSlider.Value):0} 行";
        IconSizeValueText.Text = $"{Math.Round(IconSizeSlider.Value):0} px";
        BackgroundBlurValueText.Text = BackgroundBlurSlider.Value <= 0
            ? "关闭"
            : $"{Math.Round(BackgroundBlurSlider.Value):0} px";
    }

    private LauncherSettings ReadSettingsControls()
    {
        var workArea = _activeMonitor.WorkArea;
        var settings = new LauncherSettings
        {
            FloatingSearchMode = FloatingSearchModeCheck.IsChecked == true,
            FullScreen = FullScreenCheck.IsChecked == true,
            WindowWidth = Math.Round(WidthSlider.Value),
            WindowHeight = Math.Round(HeightSlider.Value),
            AppAreaWidth = Math.Round(AppAreaWidthSlider.Value),
            AppAreaHeight = Math.Round(AppAreaHeightSlider.Value),
            Columns = (int)Math.Round(ColumnsSlider.Value),
            Rows = (int)Math.Round(RowsSlider.Value),
            IconSize = Math.Round(IconSizeSlider.Value),
            EnablePinyinSearch = PinyinSearchCheck.IsChecked == true,
            StaticBlackBackground = StaticBlackBackgroundCheck.IsChecked == true,
            BackgroundBlur = Math.Round(BackgroundBlurSlider.Value),
            HotkeyModifiers = _pendingHotkeyModifiers,
            HotkeyVirtualKey = _pendingHotkeyVirtualKey,
            HotkeyDefaultsVersion = _settings.HotkeyDefaultsVersion,
            AutoCheckUpdates = AutoUpdateCheck.IsChecked == true,
            LastUpdateCheckUtc = _settings.LastUpdateCheckUtc,
            DismissedUpdateVersion = _settings.DismissedUpdateVersion
        };
        settings.Normalize(Math.Max(800, workArea.Width), Math.Max(600, workArea.Height));
        return settings;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var next = ReadSettingsControls();
        if (!TryApplyHotkey(next.HotkeyModifiers, next.HotkeyVirtualKey))
        {
            ShowToast($"无法注册 {FormatHotkey(next.HotkeyModifiers, next.HotkeyVirtualKey)}，请更换组合键");
            return;
        }
        _settings = next;
        LauncherFooterHint.Text = $"Esc 返回桌面  ·  {FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyVirtualKey)} 随时呼出";
        try { AppDataStore.SaveSettings(_settings); ShowToast("设置已保存"); }
        catch (Exception ex) { ShowToast($"无法保存设置：{ex.Message}"); }
        _settingsSnapshot = null;
        CloseSettingsDrawer();
    }

    private void CancelSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsSnapshot is not null)
        {
            var wasPinyinEnabled = _settings.EnablePinyinSearch;
            _settings = _settingsSnapshot;
            _settingsSnapshot = null;
            if (wasPinyinEnabled != _settings.EnablePinyinSearch)
                SetPinyinSearchEnabled(_settings.EnablePinyinSearch);
            ApplyWindowSettings(_settings);
            ApplyGridSettings();
            ApplyDisplayMode();
            RenderCurrentView(true);
        }
        CloseSettingsDrawer();
    }

    private void SettingsScrim_Click(object sender, RoutedEventArgs e) => CancelSettings_Click(sender, e);

    private void CloseSettingsDrawer()
    {
        var animation = new DoubleAnimation(0, 430, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        animation.Completed += (_, _) => SettingsLayer.Visibility = Visibility.Collapsed;
        SettingsTranslate.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private void ApplyGridSettings()
    {
        AppGrid.Columns = _settings.Columns;
        AppGrid.Rows = _settings.Rows;
        var constrainArea = _settings.FullScreen && !_settings.FloatingSearchMode;
        NormalLauncherShell.Width = constrainArea
            ? Math.Min(_settings.AppAreaWidth, Math.Max(600, _activeMonitor.WorkArea.Width - 92))
            : double.NaN;
        NormalLauncherShell.Height = constrainArea
            ? Math.Min(_settings.AppAreaHeight, Math.Max(420, _activeMonitor.WorkArea.Height - 58))
            : double.NaN;
        _currentPage = Math.Max(0, Math.Min(_currentPage, Math.Max(0, (_visibleItems.Count - 1) / ItemsPerPage)));
    }

    private void ApplyWindowSettings(LauncherSettings settings)
    {
        var workArea = _activeMonitor.WorkArea;
        settings.Normalize(Math.Max(800, workArea.Width), Math.Max(600, workArea.Height));
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;

        if (settings.FullScreen || settings.FloatingSearchMode)
        {
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
        }
        else
        {
            Width = Math.Min(settings.WindowWidth, workArea.Width);
            Height = Math.Min(settings.WindowHeight, workArea.Height);
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Top + (workArea.Height - Height) / 2;
        }
        ApplyDwmCorners(!settings.FullScreen && !settings.FloatingSearchMode);
    }

    private void ApplyDwmCorners(bool rounded)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            var preference = rounded ? 2 : 1;
            DwmSetWindowAttribute(handle, 33, ref preference, sizeof(int));
        }
        catch { }
    }

    private void ApplyDisplayMode()
    {
        var floating = _settings.FloatingSearchMode;
        NormalLauncherShell.Visibility = floating ? Visibility.Collapsed : Visibility.Visible;
        SpotlightShell.Visibility = floating ? Visibility.Visible : Visibility.Collapsed;
        LauncherFooterHint.Visibility = floating ? Visibility.Collapsed : Visibility.Visible;
        BrandFooter.Visibility = floating ? Visibility.Collapsed : Visibility.Visible;

        if (floating)
        {
            ApplySpotlightTheme();
            RenderSpotlight();
        }
        else
        {
            PrimaryDimmer.Fill = BrushFrom("#72080B12");
            EdgeDimmer.Opacity = 1;
        }
        ApplyBackgroundBlur();
    }

    private void ApplyBackgroundBlur()
    {
        // A BlurEffect on a full-screen WPF element allocates several monitor-sized
        // render targets.  On high-DPI displays that can retain hundreds of MB.
        // The desktop snapshot is blurred while it is downsampled instead.
        DesktopBackground.Margin = new Thickness(0);
        DesktopBackground.Effect = null;
        if (_settings.StaticBlackBackground)
        {
            PrimaryDimmer.Fill = System.Windows.Media.Brushes.Black;
            EdgeDimmer.Opacity = 0;
        }
        if (!_capturedBackgroundBlur.Equals(_settings.BackgroundBlur) ||
            _capturedStaticBlackBackground != _settings.StaticBlackBackground)
            RefreshDesktopBackground();
    }

    private void ApplySpotlightTheme()
    {
        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            _spotlightDark = value is not int theme || theme == 0;
        }
        catch { _spotlightDark = true; }

        if (_spotlightDark)
        {
            SpotlightCard.Background = BrushFrom("#F01B1D23");
            SpotlightCard.BorderBrush = BrushFrom("#38FFFFFF");
            SpotlightTitle.Foreground = BrushFrom("#F7FFFFFF");
            SpotlightSubtitle.Foreground = BrushFrom("#91FFFFFF");
            SpotlightKeyboardHints.Foreground = BrushFrom("#78FFFFFF");
            SpotlightSearchBox.Foreground = BrushFrom("#F7FFFFFF");
            SpotlightSearchBox.CaretBrush = BrushFrom("#FFFFFFFF");
            SpotlightSearchHint.Foreground = BrushFrom("#64FFFFFF");
            SpotlightSearchIcon.Stroke = BrushFrom("#C8FFFFFF");
            SpotlightDivider.Background = BrushFrom("#22FFFFFF");
            SpotlightResultsBorder.BorderBrush = BrushFrom("#22FFFFFF");
            SpotlightShadow.Color = Colors.Black;
            SpotlightAddButton.Foreground = System.Windows.Media.Brushes.White;
            SpotlightSettingsButton.Foreground = System.Windows.Media.Brushes.White;
            SpotlightAddButton.Background = BrushFrom("#28FFFFFF");
            SpotlightSettingsButton.Background = BrushFrom("#28FFFFFF");
            SpotlightAddButton.BorderBrush = BrushFrom("#28FFFFFF");
            SpotlightSettingsButton.BorderBrush = BrushFrom("#28FFFFFF");
            PrimaryDimmer.Fill = BrushFrom("#65060910");
        }
        else
        {
            SpotlightCard.Background = BrushFrom("#F4F7F8FB");
            SpotlightCard.BorderBrush = BrushFrom("#48FFFFFF");
            SpotlightTitle.Foreground = BrushFrom("#E9000000");
            SpotlightSubtitle.Foreground = BrushFrom("#78000000");
            SpotlightKeyboardHints.Foreground = BrushFrom("#62000000");
            SpotlightSearchBox.Foreground = BrushFrom("#EA000000");
            SpotlightSearchBox.CaretBrush = BrushFrom("#E0000000");
            SpotlightSearchHint.Foreground = BrushFrom("#58000000");
            SpotlightSearchIcon.Stroke = BrushFrom("#9C000000");
            SpotlightDivider.Background = BrushFrom("#17000000");
            SpotlightResultsBorder.BorderBrush = BrushFrom("#17000000");
            SpotlightShadow.Color = BrushFrom("#A0000000").Color;
            SpotlightAddButton.Foreground = BrushFrom("#C8000000");
            SpotlightSettingsButton.Foreground = BrushFrom("#C8000000");
            SpotlightAddButton.Background = BrushFrom("#0F000000");
            SpotlightSettingsButton.Background = BrushFrom("#0F000000");
            SpotlightAddButton.BorderBrush = BrushFrom("#18000000");
            SpotlightSettingsButton.BorderBrush = BrushFrom("#18000000");
            PrimaryDimmer.Fill = BrushFrom("#380B1020");
        }
        EdgeDimmer.Opacity = .35;
    }

    private void FocusActiveSearch()
    {
        if (_settings.FloatingSearchMode)
        {
            SpotlightSearchBox.Focus();
            SpotlightSearchBox.CaretIndex = SpotlightSearchBox.Text.Length;
        }
        else
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    private void SpotlightSearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => HandleSpotlightKey(e);

    private void HandleSpotlightKey(System.Windows.Input.KeyEventArgs e)
    {
        var results = _visibleItems.Take(SpotlightMaxResults).ToList();
        switch (e.Key)
        {
            case Key.Down when results.Count > 0:
                _spotlightSelectedIndex = Math.Min(results.Count - 1, _spotlightSelectedIndex + 1);
                UpdateSpotlightSelection();
                e.Handled = true;
                break;
            case Key.Up when results.Count > 0:
                _spotlightSelectedIndex = Math.Max(0, _spotlightSelectedIndex - 1);
                UpdateSpotlightSelection();
                e.Handled = true;
                break;
            case Key.Enter when results.Count > 0 && !string.IsNullOrWhiteSpace(SpotlightSearchBox.Text):
                LaunchItem(results[_spotlightSelectedIndex]);
                e.Handled = true;
                break;
            case Key.Escape:
                if (!string.IsNullOrEmpty(SpotlightSearchBox.Text)) SpotlightSearchBox.Clear();
                else HideLauncher();
                e.Handled = true;
                break;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_settings.FloatingSearchMode && SettingsLayer.Visibility != Visibility.Visible)
        {
            HandleSpotlightKey(e);
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (SettingsLayer.Visibility == Visibility.Visible) CancelSettings_Click(sender, e);
            else HideLauncher();
            e.Handled = true;
            return;
        }
        if (SettingsLayer.Visibility == Visibility.Visible) return;
        if (e.Key == Key.Left) { ChangePage(-1); e.Handled = true; }
        if (e.Key == Key.Right) { ChangePage(1); e.Handled = true; }
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _backgroundClickCandidate = SettingsLayer.Visibility != Visibility.Visible &&
                                    e.OriginalSource is DependencyObject source &&
                                    IsBackgroundClickTarget(source);
        if (_backgroundClickCandidate) _backgroundPressPoint = e.GetPosition(this);
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_backgroundClickCandidate || e.LeftButton != MouseButtonState.Pressed) return;

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _backgroundPressPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(position.Y - _backgroundPressPoint.Y) >= SystemParameters.MinimumVerticalDragDistance)
            _backgroundClickCandidate = false;
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_backgroundClickCandidate) return;
        _backgroundClickCandidate = false;

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _backgroundPressPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(position.Y - _backgroundPressPoint.Y) >= SystemParameters.MinimumVerticalDragDistance ||
            e.OriginalSource is not DependencyObject source || !IsBackgroundClickTarget(source)) return;

        HideLauncher();
    }

    private bool IsBackgroundClickTarget(DependencyObject source)
    {
        if (SettingsLayer.Visibility == Visibility.Visible) return false;

        if (_settings.FloatingSearchMode)
            return !IsDescendantOf(source, SpotlightCard);

        return !IsDescendantOf(source, NormalSearchSurface) &&
               FindAncestor<Button>(source) is null &&
               FindAncestor<TextBox>(source) is null &&
               FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is null;
    }

    private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
    {
        for (var current = source; current is not null; current = GetParent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = GetParent(current))
            if (current is T match) return match;
        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is System.Windows.Media.Media3D.Visual3D || current is Visual)
            return VisualTreeHelper.GetParent(current);
        return LogicalTreeHelper.GetParent(current);
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SettingsLayer.Visibility != Visibility.Visible && !_settings.FloatingSearchMode) ChangePage(e.Delta < 0 ? 1 : -1);
    }

    private void ChangePage(int delta)
    {
        var pageCount = Math.Max(1, (int)Math.Ceiling(_visibleItems.Count / (double)ItemsPerPage));
        var next = Math.Clamp(_currentPage + delta, 0, pageCount - 1);
        if (next == _currentPage) return;
        _currentPage = next;
        RenderPage(true, delta);
    }

    private void ShowLauncher()
    {
        _isMinimizedToTaskbar = false;
        _activeMonitor = GetCursorMonitor();
        RefreshDesktopBackground();
        ApplyWindowSettings(_settings);
        ApplyDisplayMode();
        if (!IsVisible) Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        AnimateIn();
        FocusActiveSearch();
    }

    private bool IsLauncherOpen => IsVisible && WindowState != WindowState.Minimized;

    public void ShowFromExternalInstance()
    {
        if (!IsLauncherOpen) ShowLauncher();
        else
        {
            Topmost = true;
            Activate();
            FocusActiveSearch();
        }
    }

    private void HideLauncher()
    {
        if (SettingsLayer.Visibility == Visibility.Visible && _settingsSnapshot is not null)
        {
            var wasPinyinEnabled = _settings.EnablePinyinSearch;
            _settings = _settingsSnapshot;
            _settingsSnapshot = null;
            if (wasPinyinEnabled != _settings.EnablePinyinSearch)
                SetPinyinSearchEnabled(_settings.EnablePinyinSearch);
            SettingsLayer.Visibility = Visibility.Collapsed;
            ApplyWindowSettings(_settings);
            ApplyGridSettings();
            ApplyDisplayMode();
            RenderCurrentView();
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) =>
        {
            Topmost = false;
            _isMinimizedToTaskbar = true;
            WindowState = WindowState.Minimized;
            Root.Opacity = 1;
        };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateIn()
    {
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void AnimatePage(int direction)
    {
        var transform = new TranslateTransform();
        AppGrid.RenderTransform = transform;
        AppGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)));
        transform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(direction * 70, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void PersistItems()
    {
        try { AppDataStore.SaveItems(_allItems); }
        catch (Exception ex) { ShowToast($"无法保存应用列表：{ex.Message}"); }
    }

    private void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RefreshDesktopBackground()
    {
        DesktopBackground.Source = _settings.StaticBlackBackground
            ? null
            : CaptureDesktop(_activeMonitor.PixelBounds, _settings.BackgroundBlur);
        _capturedBackgroundBlur = _settings.BackgroundBlur;
        _capturedStaticBlackBackground = _settings.StaticBlackBackground;
    }

    private static BitmapSource? CaptureDesktop(Rectangle bounds, double blurRadius)
    {
        try
        {
            using var full = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(full)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            using var bitmap = new Bitmap(Math.Max(1, bounds.Width / 4), Math.Max(1, bounds.Height / 4), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var blurScale = blurRadius <= 0 ? 1d : Math.Clamp(1d + blurRadius / 8d, 1d, 6d);
            var sampleWidth = Math.Max(1, (int)Math.Round(bitmap.Width / blurScale));
            var sampleHeight = Math.Max(1, (int)Math.Round(bitmap.Height / blurScale));
            using var sample = new Bitmap(sampleWidth, sampleHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(sample))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(full, new Rectangle(0, 0, sample.Width, sample.Height));
            }
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = blurRadius <= 0
                    ? System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear
                    : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(sample, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }
            var handle = bitmap.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally { DeleteObject(handle); }
        }
        catch { return null; }
    }

    private static MonitorBounds GetCursorMonitor()
    {
        GetCursorPos(out var cursor);
        var monitor = MonitorFromPoint(cursor, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        uint dpiX = 96;
        uint dpiY = 96;
        try { GetDpiForMonitor(monitor, 0, out dpiX, out dpiY); } catch { }
        var scaleX = Math.Max(1, dpiX) / 96d;
        var scaleY = Math.Max(1, dpiY) / 96d;
        var pixel = new Rectangle(info.Monitor.Left, info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        var bounds = new Rect(info.Monitor.Left / scaleX, info.Monitor.Top / scaleY,
            pixel.Width / scaleX, pixel.Height / scaleY);
        var work = new Rect(info.Work.Left / scaleX, info.Work.Top / scaleY,
            (info.Work.Right - info.Work.Left) / scaleX, (info.Work.Bottom - info.Work.Top) / scaleY);
        return new MonitorBounds(pixel, bounds, work);
    }

    private readonly record struct MonitorBounds(Rectangle PixelBounds, Rect Bounds, Rect WorkArea);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
