using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace centre_app;

public sealed class AddAppsDialog : Window
{
    private static readonly SolidColorBrush MutedText = Brush("#9FFFFFFF");
    private static readonly SolidColorBrush CardBackground = Brush("#12FFFFFF");
    private static readonly SolidColorBrush CardBorder = Brush("#24FFFFFF");
    private static readonly SolidColorBrush Accent = Brush("#4F8CFF");

    private readonly ListBox _packagedList;
    private readonly TextBlock _status;
    private readonly Button _confirm;
    private readonly Grid _localPage;
    private readonly Grid _storePage;
    private readonly Button _localTab;
    private readonly Button _storeTab;
    private readonly WrapPanel _localPreviewPanel;
    private readonly List<string> _localPaths = [];
    private int _previewGeneration;

    public IReadOnlyList<string> LocalPaths => _localPaths;
    public IReadOnlyList<PackagedAppInfo> SelectedPackagedApps =>
        _packagedList.SelectedItems.Cast<PackagedAppInfo>().ToList();

    public AddAppsDialog(Window owner)
    {
        Owner = owner;
        Title = "添加应用";
        Icon = owner.Icon;
        Width = 792;
        Height = 652;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI Variable Text,Microsoft YaHei UI,Segoe UI");

        _packagedList = CreatePackagedList();
        _status = new TextBlock
        {
            Text = "选择应用文件或文件夹", Foreground = MutedText, FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        _confirm = CreateButton("添加", 104, true);
        _confirm.IsEnabled = false;
        _confirm.Click += Confirm_Click;
        _localPreviewPanel = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _localPage = CreateLocalPage();
        _storePage = CreateStorePage();
        _localTab = CreateTabButton("本地项目", true);
        _storeTab = CreateTabButton("Microsoft Store", false);
        _localTab.Click += (_, _) => SelectTab(false);
        _storeTab.Click += (_, _) => SelectTab(true);

        Content = CreateWindowSurface();
        Loaded += async (_, _) => await LoadPackagedAppsAsync();
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            DialogResult = false;
            Close();
        };
    }

    private Border CreateWindowSurface()
    {
        var surface = new Border
        {
            Margin = new Thickness(16),
            CornerRadius = new CornerRadius(26), Background = Brush("#FA191D26"),
            BorderBrush = Brush("#32FFFFFF"), BorderThickness = new Thickness(1), ClipToBounds = true
        };
        surface.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 34, ShadowDepth = 10, Opacity = .48
        };
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) });
        root.Children.Add(CreateHeader());
        var body = CreateBody();
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        var footer = CreateFooter();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        surface.Child = root;
        return surface;
    }

    private Grid CreateHeader()
    {
        var header = new Grid { Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(28, 0, 28, 0),
            Background = Brush("#14FFFFFF"),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        Grid.SetColumnSpan(divider, 3);
        header.Children.Add(divider);
        header.MouseLeftButtonDown += (_, args) => { if (args.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var mark = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(12), Background = Accent,
            Margin = new Thickness(24, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "+", FontSize = 27, FontWeight = FontWeights.Light,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -3, 0, 0)
            }
        };
        header.Children.Add(mark);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "添加应用", FontSize = 18, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "把常用应用加入你的启动中心", FontSize = 11.5, Foreground = MutedText, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        var close = CreateIconButton("×");
        close.Margin = new Thickness(0, 0, 20, 0);
        close.Click += (_, _) => { DialogResult = false; Close(); };
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        return header;
    }

    private Grid CreateBody()
    {
        var body = new Grid { Margin = new Thickness(28, 22, 28, 20) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(new TextBlock { Text = "选择添加方式", FontSize = 22, FontWeight = FontWeights.SemiBold });
        var tabs = new Border
        {
            Background = Brush("#14FFFFFF"), BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13), Padding = new Thickness(4), Margin = new Thickness(0, 17, 0, 17),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var tabPanel = new StackPanel { Orientation = Orientation.Horizontal };
        tabPanel.Children.Add(_localTab);
        tabPanel.Children.Add(_storeTab);
        tabs.Child = tabPanel;
        Grid.SetRow(tabs, 1);
        body.Children.Add(tabs);
        var pages = new Grid();
        pages.Children.Add(_localPage);
        pages.Children.Add(_storePage);
        Grid.SetRow(pages, 2);
        body.Children.Add(pages);
        return body;
    }

    private Grid CreateLocalPage()
    {
        var page = new Grid();
        var card = new Border
        {
            Background = CardBackground, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(34)
        };
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        _localPreviewPanel.Children.Add(CreateAddPreviewTile());
        panel.Children.Add(_localPreviewPanel);
        panel.Children.Add(new TextBlock
        {
            Text = "选择应用、快捷方式或文件夹", FontSize = 18, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "支持 EXE、LNK、APPREF-MS 和文件夹，可一次选择多个项目", FontSize = 12.5,
            Foreground = MutedText, Margin = new Thickness(0, 8, 0, 22), HorizontalAlignment = HorizontalAlignment.Center
        });
        var browseButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var browse = CreateButton("浏览应用…", 142, true);
        browse.Height = 42;
        browse.Margin = new Thickness(0, 0, 10, 0);
        browse.Click += Browse_Click;
        var browseFolder = CreateButton("选择文件夹…", 142, false);
        browseFolder.Height = 42;
        browseFolder.Click += BrowseFolder_Click;
        browseButtons.Children.Add(browse);
        browseButtons.Children.Add(browseFolder);
        panel.Children.Add(browseButtons);
        card.Child = panel;
        page.Children.Add(card);
        return page;
    }

    private Grid CreateStorePage()
    {
        var page = new Grid { Visibility = Visibility.Collapsed };
        page.Children.Add(new Border
        {
            Background = CardBackground, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20), Padding = new Thickness(8), Child = _packagedList
        });
        return page;
    }

    private Grid CreateFooter()
    {
        var footer = new Grid { Background = Brushes.Transparent };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(28, 0, 28, 0),
            Background = Brush("#14FFFFFF"),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        });
        _status.Margin = new Thickness(28, 0, 16, 0);
        footer.Children.Add(_status);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 28, 0)
        };
        var cancel = CreateButton("取消", 94, false);
        cancel.Margin = new Thickness(0, 0, 10, 0);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_confirm);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        return footer;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加应用", Filter = "应用和快捷方式 (*.exe;*.lnk;*.appref-ms)|*.exe;*.lnk;*.appref-ms",
            Multiselect = true, CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FileNames)
        {
            if (!_localPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) _localPaths.Add(path);
        }
        _status.Text = $"已选择 {_localPaths.Count} 个本地项目";
        _confirm.IsEnabled = _localPaths.Count > 0;
        await RenderLocalPreviewsAsync();
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "添加文件夹",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FolderNames)
        {
            if (!_localPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) _localPaths.Add(path);
        }
        _status.Text = $"已选择 {_localPaths.Count} 个本地项目";
        _confirm.IsEnabled = _localPaths.Count > 0;
        await RenderLocalPreviewsAsync();
    }

    private async Task RenderLocalPreviewsAsync()
    {
        var generation = ++_previewGeneration;
        _localPreviewPanel.Children.Clear();
        var previewPaths = _localPaths.Take(7).ToList();
        var previews = new List<(string Path, Image Image, Border Host)>();
        foreach (var path in previewPaths)
        {
            var image = new Image
            {
                Width = 58,
                Height = 58,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var host = new Border
            {
                Width = 68,
                Height = 68,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(18),
                Background = Brush("#18FFFFFF"),
                BorderBrush = Brush("#26FFFFFF"),
                BorderThickness = new Thickness(1),
                ToolTip = System.IO.Path.GetFileNameWithoutExtension(path),
                Child = image
            };
            previews.Add((path, image, host));
            _localPreviewPanel.Children.Add(host);
        }

        if (_localPaths.Count > previewPaths.Count)
        {
            _localPreviewPanel.Children.Add(new Border
            {
                Width = 68,
                Height = 68,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(18),
                Background = Brush("#18FFFFFF"),
                BorderBrush = Brush("#26FFFFFF"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = $"+{_localPaths.Count - previewPaths.Count}",
                    FontSize = 16,
                    Foreground = MutedText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
        }
        _localPreviewPanel.Children.Add(CreateAddPreviewTile());

        var tasks = previews.Select(async preview =>
        {
            var icon = await IconCacheService.LoadFilePreviewAsync(preview.Path);
            if (generation != _previewGeneration || icon is null) return;
            preview.Image.Source = icon;
        });
        await Task.WhenAll(tasks);
    }

    private Border CreateAddPreviewTile()
    {
        var button = new Button
        {
            Content = "+",
            FontSize = 38,
            FontWeight = FontWeights.Light,
            Foreground = Brush("#BFD6FF"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            ToolTip = _localPaths.Count == 0 ? "选择本地项目" : "继续添加本地项目",
            Template = CreateRoundedButtonTemplate(17)
        };
        button.Click += Browse_Click;
        return new Border
        {
            Width = 68,
            Height = 68,
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(18),
            Background = Brush("#1F4F8CFF"),
            BorderBrush = Brush("#414F8CFF"),
            BorderThickness = new Thickness(1),
            Child = button
        };
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (_localPage.Visibility == Visibility.Visible && _localPaths.Count == 0)
        {
            Browse_Click(sender, e);
            if (_localPaths.Count == 0) return;
        }
        if (_storePage.Visibility == Visibility.Visible && _packagedList.SelectedItems.Count == 0) return;
        DialogResult = true;
        Close();
    }

    private void SelectTab(bool store)
    {
        _localPage.Visibility = store ? Visibility.Collapsed : Visibility.Visible;
        _storePage.Visibility = store ? Visibility.Visible : Visibility.Collapsed;
        SetTabState(_localTab, !store);
        SetTabState(_storeTab, store);
        _confirm.IsEnabled = store ? _packagedList.SelectedItems.Count > 0 : _localPaths.Count > 0;
        if (!store) _status.Text = _localPaths.Count == 0 ? "选择应用文件或文件夹" : $"已选择 {_localPaths.Count} 个本地项目";
        else if (_packagedList.Items.Count > 0) _status.Text = $"找到 {_packagedList.Items.Count} 个可启动的 Store 应用";
    }

    private async Task LoadPackagedAppsAsync()
    {
        try
        {
            var apps = await PackagedAppService.GetLaunchableAppsAsync();
            _packagedList.ItemsSource = apps;
            if (_storePage.Visibility == Visibility.Visible) _status.Text = $"找到 {apps.Count} 个可启动的 Store 应用";
        }
        catch
        {
            if (_storePage.Visibility == Visibility.Visible) _status.Text = "无法读取 Microsoft Store 应用";
        }
    }

    private ListBox CreatePackagedList()
    {
        var list = new ListBox
        {
            SelectionMode = SelectionMode.Extended, Background = Brushes.Transparent, Foreground = Brushes.White,
            BorderThickness = new Thickness(0), Padding = new Thickness(2), ItemTemplate = CreatePackagedAppTemplate(),
            ItemContainerStyle = CreateListItemStyle()
        };
        list.SelectionChanged += (_, _) =>
        {
            _confirm.IsEnabled = list.SelectedItems.Count > 0;
            _status.Text = list.SelectedItems.Count == 0
                ? $"找到 {list.Items.Count} 个可启动的 Store 应用"
                : $"已选择 {list.SelectedItems.Count} 个 Store 应用";
        };
        return list;
    }

    private static Button CreateButton(string text, double width, bool primary) => new()
    {
        Content = text, Width = width, Height = 38, Foreground = Brushes.White,
        Background = primary ? Accent : Brush("#15FFFFFF"),
        BorderBrush = primary ? Brush("#6DA0FF") : CardBorder, BorderThickness = new Thickness(1),
        FontSize = 13, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
        Cursor = Cursors.Hand, FocusVisualStyle = null, Template = CreateRoundedButtonTemplate(11)
    };

    private static Button CreateIconButton(string text) => new()
    {
        Content = text, Width = 38, Height = 38, FontSize = 25, FontWeight = FontWeights.Light,
        Foreground = Brush("#DFFFFFFF"), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
        BorderThickness = new Thickness(0), Cursor = Cursors.Hand, FocusVisualStyle = null,
        Template = CreateRoundedButtonTemplate(12)
    };

    private static Button CreateTabButton(string text, bool selected) => new()
    {
        Content = text, Height = 34, Padding = new Thickness(18, 0, 18, 0), Margin = new Thickness(0, 0, 3, 0),
        Foreground = selected ? Brushes.White : MutedText, Background = selected ? Brush("#2BFFFFFF") : Brushes.Transparent,
        BorderThickness = new Thickness(0), FontSize = 12.5,
        FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal, Cursor = Cursors.Hand,
        FocusVisualStyle = null, Template = CreateRoundedButtonTemplate(9)
    };

    private static void SetTabState(Button button, bool selected)
    {
        button.Foreground = selected ? Brushes.White : MutedText;
        button.Background = selected ? Brush("#2BFFFFFF") : Brushes.Transparent;
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static ControlTemplate CreateRoundedButtonTemplate(double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Surface";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.OpacityProperty, .84, "Surface"));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(Border.OpacityProperty, .38, "Surface"));
        template.Triggers.Add(disabled);
        return template;
    }

    private static Style CreateListItemStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "ItemSurface";
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 5, 8, 5));
        border.SetValue(Border.MarginProperty, new Thickness(2));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#14FFFFFF"), "ItemSurface"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#294F8CFF"), "ItemSurface"));
        template.Triggers.Add(selected);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }

    private static DataTemplate CreatePackagedAppTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(Grid));
        panel.SetValue(FrameworkElement.HeightProperty, 54d);
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(FrameworkElement.WidthProperty, 42d);
        image.SetValue(FrameworkElement.HeightProperty, 42d);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 14, 0));
        image.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        image.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        image.SetBinding(Image.SourceProperty, new Binding(nameof(PackagedAppInfo.Icon)));
        panel.AppendChild(image);
        var text = new FrameworkElementFactory(typeof(StackPanel));
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(58, 0, 8, 0));
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetValue(TextBlock.FontSizeProperty, 13.5d);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(PackagedAppInfo.DisplayName)));
        text.AppendChild(title);
        var publisher = new FrameworkElementFactory(typeof(TextBlock));
        publisher.SetValue(TextBlock.FontSizeProperty, 10.5d);
        publisher.SetValue(TextBlock.ForegroundProperty, MutedText);
        publisher.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        publisher.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 4, 0, 0));
        publisher.SetBinding(TextBlock.TextProperty, new Binding(nameof(PackagedAppInfo.Publisher)));
        text.AppendChild(publisher);
        panel.AppendChild(text);
        return new DataTemplate(typeof(PackagedAppInfo)) { VisualTree = panel };
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
}
