using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace centre_app;

public sealed class AddAppsDialog : Window
{
    private readonly ListBox _packagedList;
    private readonly TextBlock _status;
    private readonly Button _confirm;
    private readonly List<string> _localPaths = [];

    public IReadOnlyList<string> LocalPaths => _localPaths;
    public IReadOnlyList<PackagedAppInfo> SelectedPackagedApps =>
        _packagedList.SelectedItems.Cast<PackagedAppInfo>().ToList();

    public AddAppsDialog(Window owner)
    {
        Owner = owner;
        Title = "添加应用";
        Icon = owner.Icon;
        Width = 720;
        Height = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(31, 35, 44));
        Foreground = Brushes.White;

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = "手动添加应用", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });

        var tabs = new TabControl { Margin = new Thickness(0, 52, 0, 16) };
        Grid.SetRow(tabs, 1);
        tabs.Items.Add(CreateLocalTab());
        _packagedList = new ListBox
        {
            SelectionMode = SelectionMode.Extended,
            Background = new SolidColorBrush(Color.FromRgb(38, 43, 54)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        _packagedList.ItemTemplate = CreatePackagedAppTemplate();
        tabs.Items.Add(new TabItem { Header = "Microsoft Store 应用", Content = _packagedList });
        root.Children.Add(tabs);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status = new TextBlock { Text = "正在读取已安装应用…", Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
        footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "取消", Width = 88, Height = 36, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        _confirm = new Button { Content = "添加", Width = 96, Height = 36 };
        _confirm.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(_confirm);
        Grid.SetColumn(buttons, 1);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        Loaded += async (_, _) => await LoadPackagedAppsAsync();
    }

    private TabItem CreateLocalTab()
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "选择 EXE、LNK 或 APPREF-MS", FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = "支持多选，也可以继续直接拖入应用中心", Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), Margin = new Thickness(0, 8, 0, 20), HorizontalAlignment = HorizontalAlignment.Center });
        var browse = new Button { Content = "浏览文件…", Width = 130, Height = 40 };
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "添加应用",
                Filter = "应用和快捷方式 (*.exe;*.lnk;*.appref-ms)|*.exe;*.lnk;*.appref-ms",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;
            _localPaths.Clear();
            _localPaths.AddRange(dialog.FileNames);
            _status.Text = $"已选择 {_localPaths.Count} 个本地应用";
        };
        panel.Children.Add(browse);
        return new TabItem { Header = "本地应用", Content = panel };
    }

    private async Task LoadPackagedAppsAsync()
    {
        try
        {
            var apps = await PackagedAppService.GetLaunchableAppsAsync();
            _packagedList.ItemsSource = apps;
            _status.Text = $"找到 {apps.Count} 个可启动的 Store 应用";
        }
        catch
        {
            _status.Text = "无法读取 Microsoft Store 应用";
        }
    }

    private static DataTemplate CreatePackagedAppTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6));

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetValue(FrameworkElement.WidthProperty, 40d);
        image.SetValue(FrameworkElement.HeightProperty, 40d);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 12, 0));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(PackagedAppInfo.Icon)));
        panel.AppendChild(image);

        var text = new FrameworkElementFactory(typeof(StackPanel));
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetValue(TextBlock.FontSizeProperty, 14d);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(PackagedAppInfo.DisplayName)));
        text.AppendChild(title);
        var publisher = new FrameworkElementFactory(typeof(TextBlock));
        publisher.SetValue(TextBlock.FontSizeProperty, 11d);
        publisher.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)));
        publisher.SetBinding(TextBlock.TextProperty, new Binding(nameof(PackagedAppInfo.Publisher)));
        text.AppendChild(publisher);
        panel.AppendChild(text);
        return new DataTemplate(typeof(PackagedAppInfo)) { VisualTree = panel };
    }
}
