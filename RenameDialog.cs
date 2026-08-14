using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace centre_app;

public sealed class RenameDialog : Window
{
    private readonly TextBox _textBox;

    public string Result => _textBox.Text.Trim();

    public RenameDialog(Window owner, string currentName)
    {
        Owner = owner;
        Title = "重命名";
        Width = 380;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;

        _textBox = new TextBox
        {
            Text = currentName,
            FontSize = 16,
            Margin = new Thickness(0, 14, 0, 18),
            Padding = new Thickness(10, 7, 10, 7),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 104, 120))
        };

        var cancel = new Button { Content = "取消", Width = 88, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var confirm = new Button { Content = "保存", Width = 88, Height = 34, IsDefault = true };
        confirm.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(Result)) DialogResult = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = "应用名称", Foreground = System.Windows.Media.Brushes.White, FontSize = 14 });
        content.Children.Add(_textBox);
        content.Children.Add(buttons);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(35, 39, 49)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(78, 86, 102)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, Opacity = .55, ShadowDepth = 8 }
        };

        Loaded += (_, _) => { _textBox.Focus(); _textBox.SelectAll(); };
    }
}
