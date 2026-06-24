using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Muselly.App.ViewModels;

namespace Muselly.App;

/// <summary>
/// Resolves a view for a view model by naming convention (<c>…ViewModels.FooViewModel</c> →
/// <c>…Views.FooView</c>). Registered in <c>App.DataTemplates</c> so a <c>ContentControl</c> bound to a
/// view model automatically renders the matching view.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
            return new TextBlock { Text = "null" };

        var name = data.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(name);
        if (type is not null && Activator.CreateInstance(type) is Control control)
            return control;

        return new TextBlock { Text = "View not found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
