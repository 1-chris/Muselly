using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Muselly.App.Theming;

namespace Muselly.App.Controls;

/// <summary>
/// A vertical, self-drawn EQ band fader. <see cref="Value"/> is a gain in dB between <see cref="Minimum"/>
/// and <see cref="Maximum"/> (bindable two-way); the fill grows from the 0 dB centre line up or down, with
/// a hover/drag thumb. Matches the Catppuccin look of the other custom audio controls.
/// </summary>
public sealed class EqSlider : ThemedControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<EqSlider, double>(nameof(Value), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<EqSlider, double>(nameof(Minimum), -12);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<EqSlider, double>(nameof(Maximum), 12);

    private IBrush? _grooveBrush;
    private IBrush? _fillBrush;
    private IBrush? _thumbBrush;
    private IPen? _centerPen;
    private bool _hover;
    private bool _dragging;

    public EqSlider()
    {
        Width = 30;
        Height = 150;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }

    static EqSlider()
    {
        AffectsRender<EqSlider>(ValueProperty, MinimumProperty, MaximumProperty);
    }

    protected override void BuildThemeResources()
    {
        _grooveBrush = new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Surface2, 150));
        _fillBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ThemePalette.Mauve, 0),
                new GradientStop(ThemePalette.Blue, 1)
            }
        };
        _thumbBrush = new SolidColorBrush(ThemePalette.Text);
        _centerPen = new Pen(new SolidColorBrush(ThemePalette.WithAlpha(ThemePalette.Overlay0, 180)), 1);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_grooveBrush is null) BuildThemeResources();

        const double thumbR = 7;
        const double grooveW = 5;
        var cx = Bounds.Width / 2;
        var top = thumbR;
        var bottom = Bounds.Height - thumbR;
        var trackH = Math.Max(1, bottom - top);

        var range = Math.Max(0.0001, Maximum - Minimum);
        var valFrac = Math.Clamp((Value - Minimum) / range, 0, 1);
        var zeroFrac = Math.Clamp((0 - Minimum) / range, 0, 1);
        var y = bottom - valFrac * trackH;
        var yZero = bottom - zeroFrac * trackH;

        // Groove.
        context.DrawRectangle(_grooveBrush, null,
            new RoundedRect(new Rect(cx - grooveW / 2, top, grooveW, trackH), grooveW / 2));

        // Fill between the 0 dB line and the value.
        var fillTop = Math.Min(y, yZero);
        var fillH = Math.Abs(y - yZero);
        if (fillH > 0.5)
            context.DrawRectangle(_fillBrush, null,
                new RoundedRect(new Rect(cx - grooveW / 2, fillTop, grooveW, fillH), grooveW / 2));

        // 0 dB reference line.
        context.DrawLine(_centerPen!, new Point(cx - 8, yZero), new Point(cx + 8, yZero));

        // Thumb.
        if (_hover || _dragging || Math.Abs(Value) > 0.01)
            context.DrawEllipse(_thumbBrush, null, new Point(cx, y), thumbR, thumbR);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        SetFromY(e.GetPosition(this).Y);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFromY(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void SetFromY(double py)
    {
        const double thumbR = 7;
        var top = thumbR;
        var bottom = Bounds.Height - thumbR;
        var trackH = Math.Max(1, bottom - top);
        var frac = Math.Clamp((bottom - py) / trackH, 0, 1);
        var v = Minimum + frac * (Maximum - Minimum);
        if (Math.Abs(v) < 0.8) v = 0; // gentle detent at 0 dB
        Value = Math.Round(v, 1);
    }
}
