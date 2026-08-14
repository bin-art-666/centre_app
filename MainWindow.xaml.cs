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
        RenderPage();
        AnimateIn();
        SearchBox.Focus();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        if (!RegisterHotKey(handle, HotkeyId, 0x0004, 0x09))
            ShowToast("Shift + Tab 已被其他程序占用");
        ApplyDwmCorners(!_settings.FullScreen);
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
            RenderPage(true);
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
        RenderPage(true);
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
            RenderPage(true);
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
        RenderPage(true);
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
        RenderPage(true, -1);
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
        RenderPage(true);
        e.Effects = System.Windows.DragDropEffects.Move;
        e.Handled = true;
    }

    private void LauncherButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressNextClick) { _suppressNextClick = false; return; }
        if (sender is not Button { Tag: LauncherItemData item }) return;
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
        if (!_isLoaded) return;
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        _currentPage = 0;
        RefreshFilter();
        RenderPage(true);
    }

    private void RefreshFilter()
    {
        var query = SearchBox.Text.Trim();
        _visibleItems = query.Length == 0
            ? [.. _allItems]
            : _allItems.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void ClearSearchButton_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsLayer.Visibility == Visibility.Visible) return;
        _settingsSnapshot = _settings.Clone();
        var workArea = SystemParameters.WorkArea;
        _isSettingsInitializing = true;
        WidthSlider.Maximum = Math.Max(800, workArea.Width);
        HeightSlider.Maximum = Math.Max(600, workArea.Height);
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
        RenderPage(true);
    }

    private void UpdateSettingsLabels()
    {
        WindowSizePanel.IsEnabled = FullScreenCheck.IsChecked != true;
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
            RenderPage(true);
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

        if (settings.FullScreen)
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
        ApplyDwmCorners(!settings.FullScreen);
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

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
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
        if (SettingsLayer.Visibility != Visibility.Visible) ChangePage(e.Delta < 0 ? 1 : -1);
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
        Show();
        Activate();
        Topmost = true;
        AnimateIn();
        SearchBox.Focus();
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
            RenderPage();
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
