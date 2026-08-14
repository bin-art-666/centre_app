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
    private const string InternalDragFormat = "Centre.LauncherItem";
    private const int SpotlightMaxResults = 7;
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".appref-ms"
    };

    private readonly List<LauncherItemData> _allItems = [];
    private List<LauncherItemData> _visibleItems = [];
    private readonly SemaphoreSlim _iconLoader = new(4);
    private readonly HashSet<Guid> _loadingIcons = [];
    private readonly DispatcherTimer _toastTimer;
    private LauncherSettings _settings;
    private LauncherSettings? _settingsSnapshot;
    private int _currentPage;
    private HwndSource? _source;
    private System.Windows.Point _dragStart;
    private LauncherItemData? _pressedItem;
    private bool _suppressNextClick;
    private bool _isLoaded;
    private bool _isSettingsInitializing;
    private bool _syncingSearch;
    private bool _spotlightDark = true;
    private int _spotlightSelectedIndex;

    private int ItemsPerPage => _settings.Rows * _settings.Columns;

    public MainWindow()
    {
        var desktop = CaptureDesktop();
        InitializeComponent();
        DesktopBackground.Source = desktop;

        var workArea = SystemParameters.WorkArea;
        _settings = AppDataStore.LoadSettings();
        _settings.Normalize(Math.Max(800, workArea.Width), Math.Max(600, workArea.Height));
        ApplyWindowSettings(_settings);

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
        _toastTimer.Tick += (_, _) => { _toastTimer.Stop(); ToastBorder.Visibility = Visibility.Collapsed; };

        Loaded += MainWindow_Loaded;
        SourceInitialized += MainWindow_SourceInitialized;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _allItems.AddRange(AppDataStore.LoadItems().Where(item =>
            item is not null && item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.TargetPath)));
        _visibleItems = [.. _allItems];
        _isLoaded = true;
        ApplyGridSettings();
        ApplyDisplayMode();
        RenderCurrentView();
        AnimateIn();
        FocusActiveSearch();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        if (!RegisterHotKey(handle, HotkeyId, 0x0004, 0x09))
            ShowToast("Shift + Tab 已被其他程序占用");
        ApplyDwmCorners(!_settings.FullScreen && !_settings.FloatingSearchMode);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(handle, HotkeyId);
        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            if (IsVisible) HideLauncher(); else ShowLauncher();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加应用",
            Filter = "应用和快捷方式 (*.exe;*.lnk;*.appref-ms)|*.exe;*.lnk;*.appref-ms",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) AddFiles(dialog.FileNames);
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

            _allItems.Add(new LauncherItemData
            {
                Id = Guid.NewGuid(),
                Name = GetDefaultName(path),
                TargetPath = path
            });
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
    private void Window_DragOver(object sender, System.Windows.DragEventArgs e) => SetDragEffect(e);

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
        var results = expanded ? _visibleItems.Take(SpotlightMaxResults).ToList() : [];
        _spotlightSelectedIndex = results.Count == 0 ? 0 : Math.Clamp(_spotlightSelectedIndex, 0, results.Count - 1);
        SpotlightResults.Children.Clear();

        if (expanded && results.Count == 0)
        {
            SpotlightResults.Children.Add(new TextBlock
            {
                Text = "没有找到匹配的应用",
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

        SpotlightResultsBorder.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        SpotlightSubtitle.Text = _allItems.Count == 0
            ? "点击 + 添加你的第一个应用"
            : expanded
                ? $"找到 {_visibleItems.Count} 个结果"
                : "快速查找并启动应用";

        var resultHeight = results.Count == 0 ? 78 : results.Count * 72 + 18;
        var targetHeight = expanded ? Math.Min(730, 190 + resultHeight) : 190;
        if (animate)
        {
            SpotlightCard.BeginAnimation(HeightProperty,
                new DoubleAnimation(SpotlightCard.ActualHeight > 0 ? SpotlightCard.ActualHeight : SpotlightCard.Height, targetHeight,
                    TimeSpan.FromMilliseconds(260)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });
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
            CornerRadius = new CornerRadius(13),
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
            Width = iconSize,
            Height = iconSize,
            Stretch = Stretch.Uniform,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = .28 }
        };
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
            Text = item.TargetPath,
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
            CornerRadius = new CornerRadius(Math.Max(12, _settings.IconSize * .23)),
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
            Width = _settings.IconSize,
            Height = _settings.IconSize,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = Math.Max(8, _settings.IconSize * .16),
                ShadowDepth = 4,
                Opacity = .43,
                Color = Colors.Black
            }
        };
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
        if (item.Icon is null && _loadingIcons.Add(item.Id))
        {
            await _iconLoader.WaitAsync();
            try { item.Icon = await Task.Run(() => LoadItemIcon(item)); }
            finally { _iconLoader.Release(); _loadingIcons.Remove(item.Id); }
        }

        if (!image.IsLoaded && !image.IsVisible) return;
        image.Source = item.Icon;
        fallback.Visibility = item.Icon is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static BitmapSource? LoadItemIcon(LauncherItemData item)
    {
        if (!string.IsNullOrWhiteSpace(item.CustomIconPath) && File.Exists(item.CustomIconPath))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(Path.GetFullPath(item.CustomIconPath));
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { }
        }
        return GetShellIcon(item.TargetPath);
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
        item.CustomIconPath = null;
        item.Icon = null;
        PersistItems();
        RenderCurrentView(true);
        ShowToast("已恢复默认图标");
    }

    private void RemoveItem(LauncherItemData item)
    {
        if (System.Windows.MessageBox.Show(this, $"从 Centre 中删除“{item.Name}”？\n原始程序不会被删除。", "删除应用",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        AppDataStore.DeleteCustomIcon(item);
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
        System.Windows.DragDrop.DoDragDrop((DependencyObject)sender, data, System.Windows.DragDropEffects.Move);
        _pressedItem = null;
        var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        resetTimer.Tick += (_, _) => { resetTimer.Stop(); _suppressNextClick = false; };
        resetTimer.Start();
    }

    private static void LauncherButton_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalDragFormat)) return;
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
    }

    private void LauncherButton_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDraggedItem(e, out var source) || (sender as Button)?.Tag is not LauncherItemData target || ReferenceEquals(source, target)) return;
        _allItems.Remove(source);
        var targetIndex = _allItems.IndexOf(target);
        _allItems.Insert(Math.Max(0, targetIndex), source);
        FinishReorder(e);
    }

    private void AppGrid_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(InternalDragFormat)) return;
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
    }

    private void AppGrid_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!TryGetDraggedItem(e, out var source)) return;
        var insertionIndex = Math.Min((_currentPage + 1) * ItemsPerPage, _allItems.Count);
        var oldIndex = _allItems.IndexOf(source);
        _allItems.Remove(source);
        if (oldIndex < insertionIndex) insertionIndex--;
        _allItems.Insert(Math.Clamp(insertionIndex, 0, _allItems.Count), source);
        FinishReorder(e);
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
    }

    private void LauncherButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressNextClick) { _suppressNextClick = false; return; }
        if (sender is not Button { Tag: LauncherItemData item }) return;
        LaunchItem(item);
    }

    private void LaunchItem(LauncherItemData item)
    {
        if (!File.Exists(item.TargetPath))
        {
            ShowToast($"找不到“{item.Name}”的目标文件");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(item.TargetPath) { UseShellExecute = true });
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
        _visibleItems = query.Length == 0
            ? [.. _allItems]
            : _allItems.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.FloatingSearchMode) SpotlightSearchBox.Clear(); else SearchBox.Clear();
        FocusActiveSearch();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsLayer.Visibility == Visibility.Visible) return;
        _settingsSnapshot = _settings.Clone();
        var workArea = SystemParameters.WorkArea;
        _isSettingsInitializing = true;
        WidthSlider.Maximum = Math.Max(800, workArea.Width);
        HeightSlider.Maximum = Math.Max(600, workArea.Height);
        FloatingSearchModeCheck.IsChecked = _settings.FloatingSearchMode;
        FullScreenCheck.IsChecked = _settings.FullScreen;
        WidthSlider.Value = Math.Clamp(_settings.WindowWidth, WidthSlider.Minimum, WidthSlider.Maximum);
        HeightSlider.Value = Math.Clamp(_settings.WindowHeight, HeightSlider.Minimum, HeightSlider.Maximum);
        ColumnsSlider.Value = _settings.Columns;
        RowsSlider.Value = _settings.Rows;
        IconSizeSlider.Value = _settings.IconSize;
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
        _settings = ReadSettingsControls();
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
        WidthValueText.Text = $"{Math.Round(WidthSlider.Value):0} px";
        HeightValueText.Text = $"{Math.Round(HeightSlider.Value):0} px";
        ColumnsValueText.Text = $"{Math.Round(ColumnsSlider.Value):0} 列";
        RowsValueText.Text = $"{Math.Round(RowsSlider.Value):0} 行";
        IconSizeValueText.Text = $"{Math.Round(IconSizeSlider.Value):0} px";
    }

    private LauncherSettings ReadSettingsControls()
    {
        var workArea = SystemParameters.WorkArea;
        var settings = new LauncherSettings
        {
            FloatingSearchMode = FloatingSearchModeCheck.IsChecked == true,
            FullScreen = FullScreenCheck.IsChecked == true,
            WindowWidth = Math.Round(WidthSlider.Value),
            WindowHeight = Math.Round(HeightSlider.Value),
            Columns = (int)Math.Round(ColumnsSlider.Value),
            Rows = (int)Math.Round(RowsSlider.Value),
            IconSize = Math.Round(IconSizeSlider.Value)
        };
        settings.Normalize(Math.Max(800, workArea.Width), Math.Max(600, workArea.Height));
        return settings;
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = ReadSettingsControls();
        try { AppDataStore.SaveSettings(_settings); ShowToast("设置已保存"); }
        catch (Exception ex) { ShowToast($"无法保存设置：{ex.Message}"); }
        _settingsSnapshot = null;
        CloseSettingsDrawer();
    }

    private void CancelSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsSnapshot is not null)
        {
            _settings = _settingsSnapshot;
            _settingsSnapshot = null;
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
        _currentPage = Math.Max(0, Math.Min(_currentPage, Math.Max(0, (_visibleItems.Count - 1) / ItemsPerPage)));
    }

    private void ApplyWindowSettings(LauncherSettings settings)
    {
        var workArea = SystemParameters.WorkArea;
        settings.Normalize(Math.Max(800, workArea.Width), Math.Max(600, workArea.Height));
        WindowState = WindowState.Normal;
        ResizeMode = ResizeMode.NoResize;

        if (settings.FullScreen || settings.FloatingSearchMode)
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
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
        DesktopBackground.Source = CaptureDesktop();
        ApplyWindowSettings(_settings);
        ApplyDisplayMode();
        Show();
        Activate();
        Topmost = true;
        AnimateIn();
        FocusActiveSearch();
    }

    private void HideLauncher()
    {
        if (SettingsLayer.Visibility == Visibility.Visible && _settingsSnapshot is not null)
        {
            _settings = _settingsSnapshot;
            _settingsSnapshot = null;
            SettingsLayer.Visibility = Visibility.Collapsed;
            ApplyWindowSettings(_settings);
            ApplyGridSettings();
            ApplyDisplayMode();
            RenderCurrentView();
        }
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140));
        fade.Completed += (_, _) => { Hide(); Root.Opacity = 1; };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateIn()
    {
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.08, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
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

    private static BitmapSource? CaptureDesktop()
    {
        try
        {
            var bounds = new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
            using var full = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(full)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            using var bitmap = new Bitmap(Math.Max(1, bounds.Width / 4), Math.Max(1, bounds.Height / 4), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(full, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
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

    private static BitmapSource? GetShellIcon(string path)
    {
        var info = new ShFileInfo();
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), 0x000000100);
        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.IconHandle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(128, 128));
            source.Freeze();
            return source;
        }
        finally { DestroyIcon(info.IconHandle); }
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
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
