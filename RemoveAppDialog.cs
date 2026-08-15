using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace centre_app;

public sealed class RemoveAppDialog : Window
{
    private static readonly SolidColorBrush MutedText = Brush("#9FFFFFFF");

    public RemoveAppDialog(Window owner, string appName)
    {
        Owner = owner;
        Title = "删除应用";
        Width = 502;
        Height = 314;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        ShowInTaskbar = false;
        FontFamily = new FontFamily("Segoe UI Variable Text,Microsoft YaHei UI,Segoe UI");

        Content = CreateSurface(appName);
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            DialogResult = false;
            Close();
        };
    }

    private Border CreateSurface(string appName)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(66) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(74) });
        root.Children.Add(CreateHeader());

        var content = new Grid { Margin = new Thickness(26, 21, 26, 18) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var warning = new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(17),
            Background = Brush("#25FF5F6D"),
            BorderBrush = Brush("#4AFF7380"),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = "!",
                Foreground = Brush("#FFFFA2AA"),
                FontSize = 30,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        content.Children.Add(warning);
        var text = new StackPanel { Margin = new Thickness(17, 1, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = $"移除“{appName}”？",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        text.Children.Add(new TextBlock
        {
            Text = "该项目将从应用中心移除。",
            FontSize = 13,
            Foreground = MutedText,
            Margin = new Thickness(0, 9, 0, 0)
        });
        text.Children.Add(new TextBlock
        {
            Text = "原始程序、快捷方式或文件夹不会被删除。",
            FontSize = 12,
            Foreground = Brush("#70FFFFFF"),
            Margin = new Thickness(0, 5, 0, 0)
        });
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var footer = new Grid { Background = Brushes.Transparent };
        footer.Children.Add(new Border
        {
            Height = 1,
            Margin = new Thickness(24, 0, 24, 0),
            Background = Brush("#14FFFFFF"),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 24, 0)
        };
        var cancel = CreateButton("取消", false);
        cancel.Margin = new Thickness(0, 0, 10, 0);
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var remove = CreateButton("移除", true);
        remove.IsDefault = true;
        remove.Click += (_, _) => { DialogResult = true; Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(remove);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        return new Border
        {
            Margin = new Thickness(16),
            Background = Brush("#FC191D26"),
            BorderBrush = Brush("#32FFFFFF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            ClipToBounds = true,
            Child = root,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 32,
                ShadowDepth = 10,
                Opacity = .5
            }
        };
    }

    private Grid CreateHeader()
    {
        var header = new Grid { Background = Brushes.Transparent };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var divider = new Border
        {
            Height = 1,
            Margin = new Thickness(24, 0, 24, 0),
            Background = Brush("#14FFFFFF"),
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        Grid.SetColumnSpan(divider, 2);
        header.Children.Add(divider);
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        header.Children.Add(new TextBlock
        {
            Text = "删除应用",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(24, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new Button
        {
            Content = "×",
            Width = 38,
            Height = 38,
            Margin = new Thickness(0, 0, 18, 0),
            FontSize = 24,
            FontWeight = FontWeights.Light,
            Foreground = Brush("#DFFFFFFF"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            FocusVisualStyle = null,
            Template = CreateButtonTemplate(12)
        };
        close.Click += (_, _) => { DialogResult = false; Close(); };
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        return header;
    }

    private static Button CreateButton(string text, bool destructive) => new()
    {
        Content = text,
        Width = 96,
        Height = 38,
        Foreground = Brushes.White,
        Background = destructive ? Brush("#E5525F") : Brush("#15FFFFFF"),
        BorderBrush = destructive ? Brush("#FF7380") : Brush("#28FFFFFF"),
        BorderThickness = new Thickness(1),
        FontSize = 13,
        FontWeight = destructive ? FontWeights.SemiBold : FontWeights.Normal,
        Cursor = Cursors.Hand,
        FocusVisualStyle = null,
        Template = CreateButtonTemplate(11)
    };

    private static ControlTemplate CreateButtonTemplate(double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.Name = "Surface";
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, .84, "Surface"));
        template.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .66, "Surface"));
        template.Triggers.Add(pressed);
        return template;
    }

    private static SolidColorBrush Brush(string value)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
        brush.Freeze();
        return brush;
    }
}
