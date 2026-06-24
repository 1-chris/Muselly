using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Muselly.App.Theming;

namespace Muselly.App.Controls;

/// <summary>
/// The custom audio scrobbler: a fully self-drawn seek bar that shows playback progress and lets the user
/// scrub. It paints a rounded groove, a theme-gradient played portion and a thumb that grows on hover. The
/// thumb follows the pointer while dragging (the bound <see cref="Position"/> is ignored mid-drag so it
/// doesn't fight the user), and a <see cref="SeekCommand"/> is invoked with the target <see cref="TimeSpan"/>
/// on release (and on a simple click). It repaints automatically on theme changes via <see cref="ThemedControl"/>.
/// </summary>
public sealed class Scrobbler : ThemedControl
{
    public static readonly StyledProperty<TimeSpan> PositionProperty =
        AvaloniaProperty.Register<Scrobbler, TimeSpan>(nameof(Position));

    public static readonly StyledProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.Register<Scrobbler, TimeSpan>(nameof(Duration));

    public static readonly StyledProperty<ICommand?> SeekCommandProperty =
        AvaloniaProperty.Register<Scrobbler, ICommand?>(nameof(SeekCommand));

    private IBrush? _grooveBrush;
    private IBrush? _fillBrush;
    private IBrush? _thumbBrush;
    private IBrush? _thumbRing;

    private bool _hover;
    private bool _dragging;
    private double _previewFraction;

    public Scrobbler()
    {
        Height = 22;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public TimeSpan Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public TimeSpan Duration
    {
        get => GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public ICommand? SeekCommand
    {
        get => GetValue(SeekCommandProperty);
        set => SetValue(SeekCommandProperty, value);
    }

    static Scrobbler()
    {
        AffectsRender<Scrobbler>(PositionProperty, DurationProperty);
    }

    protected override void BuildThemeResources()
    {
        _grooveBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Surface2, 140));
        _fillBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ThemePalette.Mauve, 0),
                new GradientStop(ThemePalette.Pink, 1)
            }
        };
        _thumbBrush = new SolidColorBrush(ThemePalette.Text);
        _thumbRing = new SolidColorBrush(ThemePalette.Mauve);
    }

    private double CurrentFraction()
    {
        if (_dragging) return _previewFraction;
        var dur = Duration.TotalSeconds;
        return dur > 0 ? Math.Clamp(Position.TotalSeconds / dur, 0, 1) : 0;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_grooveBrush is null) BuildThemeResources();

        const double thumbRadius = 7;
        var pad = thumbRadius;
        var width = Bounds.Width;
        var height = Bounds.Height;
        var trackLeft = pad;
        var trackRight = Math.Max(pad, width - pad);
        var trackWidth = Math.Max(0, trackRight - trackLeft);
        var cy = height / 2;
        var grooveH = (_hover || _dragging) ? 6.0 : 5.0;

        var fraction = CurrentFraction();
        var fillX = trackLeft + fraction * trackWidth;

        // Groove (full track).
        var groove = new RoundedRect(new Rect(trackLeft, cy - grooveH / 2, trackWidth, grooveH), grooveH / 2);
        context.DrawRectangle(_grooveBrush, null, groove);

        // Played portion.
        if (fillX > trackLeft)
        {
            var fill = new RoundedRect(new Rect(trackLeft, cy - grooveH / 2, fillX - trackLeft, grooveH), grooveH / 2);
            context.DrawRectangle(_fillBrush, null, fill);
        }

        // Thumb (only while hovering/dragging for a clean resting state).
        if (_hover || _dragging)
        {
            context.DrawEllipse(_thumbBrush, null, new Point(fillX, cy), thumbRadius, thumbRadius);
            context.DrawEllipse(null, new Pen(_thumbRing, 2), new Point(fillX, cy), thumbRadius, thumbRadius);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        _previewFraction = FractionAt(e.GetPosition(this).X);
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        _previewFraction = FractionAt(e.GetPosition(this).X);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);

        var target = TimeSpan.FromSeconds(_previewFraction * Duration.TotalSeconds);
        if (SeekCommand is { } cmd && cmd.CanExecute(target))
            cmd.Execute(target);

        InvalidateVisual();
    }

    private double FractionAt(double x)
    {
        const double pad = 7;
        var trackLeft = pad;
        var trackWidth = Math.Max(1, Bounds.Width - pad * 2);
        return Math.Clamp((x - trackLeft) / trackWidth, 0, 1);
    }
}
