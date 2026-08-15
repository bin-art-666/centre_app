using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace centre_app;

/// <summary>
/// Hosts a frozen snapshot of the dragged item in a separate popup window.
/// Keeping the preview out of the launcher's blurred visual tree prevents
/// stale adorner frames from being retained by the desktop compositor.
/// </summary>
public sealed class DragPreviewPopup : IDisposable
{
    private readonly Popup _popup;
    private readonly double _width;
    private readonly double _height;

    public DragPreviewPopup(FrameworkElement placementTarget, FrameworkElement preview)
    {
        _width = Math.Max(1, preview.ActualWidth);
        _height = Math.Max(1, preview.ActualHeight);

        var dpi = VisualTreeHelper.GetDpi(preview);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(_width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(_height * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(preview);
        bitmap.Freeze();

        _popup = new Popup
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.RelativePoint,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.None,
            IsHitTestVisible = false,
            Child = new Image
            {
                Source = bitmap,
                Width = _width,
                Height = _height,
                Opacity = .64,
                IsHitTestVisible = false,
                SnapsToDevicePixels = true
            }
        };
    }

    public void Show(Point position)
    {
        Update(position);
        _popup.IsOpen = true;
    }

    public void Update(Point position)
    {
        _popup.HorizontalOffset = position.X - _width / 2;
        _popup.VerticalOffset = position.Y - _height / 2;
    }

    public void Dispose()
    {
        _popup.IsOpen = false;
        _popup.Child = null;
    }
}
