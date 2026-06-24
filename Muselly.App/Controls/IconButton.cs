using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Muselly.App.Controls;

/// <summary>
/// A button whose content is a single vector glyph. The default look is a transparent, rounded hit target
/// that tints on hover; style classes change the shape/emphasis:
/// <list type="bullet">
///   <item><c>accent</c> — filled accent circle (the primary play button).</item>
///   <item><c>pill</c> — rounded rectangle used for toolbar actions.</item>
///   <item><c>active</c> — shows the "on" state for toggles like shuffle/repeat.</item>
/// </list>
/// The glyph is supplied via <see cref="Icon"/> (a <see cref="Geometry"/>, usually from the icon resource
/// dictionary) and painted with <see cref="Control.Foreground"/> so it follows the theme.
/// </summary>
public class IconButton : Button
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<IconButton, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<IconButton, double>(nameof(IconSize), 20d);

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(IconButton);
}
