using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Muselly.App.Controls;

/// <summary>
/// Attached behaviour that invokes a command when a control is double-tapped (double-clicked). Used on the
/// row templates of every track list so double-clicking a song behaves exactly like pressing its play
/// button. Bind <see cref="CommandProperty"/> (and optionally <see cref="CommandParameterProperty"/>) on the
/// row container.
/// </summary>
public static class DoubleTappedBehavior
{
    public static readonly AttachedProperty<ICommand?> CommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>("Command", typeof(DoubleTappedBehavior));

    public static readonly AttachedProperty<object?> CommandParameterProperty =
        AvaloniaProperty.RegisterAttached<Control, object?>("CommandParameter", typeof(DoubleTappedBehavior));

    static DoubleTappedBehavior()
    {
        CommandProperty.Changed.AddClassHandler<Control>(OnCommandChanged);
    }

    public static void SetCommand(Control control, ICommand? value) => control.SetValue(CommandProperty, value);
    public static ICommand? GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommandParameter(Control control, object? value) => control.SetValue(CommandParameterProperty, value);
    public static object? GetCommandParameter(Control control) => control.GetValue(CommandParameterProperty);

    private static void OnCommandChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        // Detach first so re-binding doesn't double-subscribe.
        control.DoubleTapped -= OnDoubleTapped;
        if (e.NewValue is ICommand)
            control.DoubleTapped += OnDoubleTapped;
    }

    private static void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control control) return;
        var command = GetCommand(control);
        if (command is null) return;
        var parameter = GetCommandParameter(control);
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
            e.Handled = true;
        }
    }
}
