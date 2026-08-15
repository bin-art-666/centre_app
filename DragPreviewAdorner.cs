using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace centre_app;

public sealed class DragPreviewAdorner : Adorner
{
    private readonly VisualBrush _brush;
    private readonly Size _size;
    private Point _position;

    public DragPreviewAdorner(UIElement adornedElement, UIElement preview) : base(adornedElement)
    {
        IsHitTestVisible = false;
        _brush = new VisualBrush(preview) { Opacity = .58, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
        _size = preview.RenderSize;
    }

    public void Update(Point position)
    {
        _position = position;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRoundedRectangle(_brush, null,
            new Rect(_position.X - _size.Width / 2, _position.Y - _size.Height / 2, _size.Width, _size.Height), 14, 14);
    }
}
