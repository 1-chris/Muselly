using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Muselly.App.Theming;

namespace Muselly.App.Controls;

/// <summary>
/// A compact, self-drawn horizontal level slider used for volume. <see cref="Value"/> is a 0–1 fraction,
/// bindable two-way; dragging or clicking updates it live. Visually it mirrors the <see cref="Scrobbler"/>
/// (rounded groove + theme-gradient fill + hover thumb) so the player's controls feel like one family.
/// </summary>
public sealed class LevelSlider : ThemedControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LevelSlider, double>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private IBrush? _grooveBrush;
    private IBrush? _fillBrush;
    private IBrush? _thumbBrush;
    private bool _hover;
    private bool _dragging;

    public LevelSlider()
    {
        Height = 18;
        MinWidth = 80;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    static LevelSlider()
    {
        AffectsRender<LevelSlider>(ValueProperty);
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
                new GradientStop(ThemePalette.Blue, 0),
                new GradientStop(ThemePalette.Mauve, 1)
            }
        };
        _thumbBrush = new SolidColorBrush(ThemePalette.Text);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_grooveBrush is null) BuildThemeResources();

        const double thumbRadius = 6;
        var pad = thumbRadius;
        var trackLeft = pad;
        var trackWidth = Math.Max(0, Bounds.Width - pad * 2);
        var cy = Bounds.Height / 2;
        var grooveH = 4.0;

        var fraction = Math.Clamp(Value, 0, 1);
        var fillX = trackLeft + fraction * trackWidth;

        context.DrawRectangle(_grooveBrush, null,
            new RoundedRect(new Rect(trackLeft, cy - grooveH / 2, trackWidth, grooveH), grooveH / 2));

        if (fillX > trackLeft)
            context.DrawRectangle(_fillBrush, null,
                new RoundedRect(new Rect(trackLeft, cy - grooveH / 2, fillX - trackLeft, grooveH), grooveH / 2));

        if (_hover || _dragging)
            context.DrawEllipse(_thumbBrush, null, new Point(fillX, cy), thumbRadius, thumbRadius);
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
        Value = FractionAt(e.GetPosition(this).X);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        Value = FractionAt(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private double FractionAt(double x)
    {
        const double pad = 6;
        var trackWidth = Math.Max(1, Bounds.Width - pad * 2);
        return Math.Clamp((x - pad) / trackWidth, 0, 1);
    }
}
