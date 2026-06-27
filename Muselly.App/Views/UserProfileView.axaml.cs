using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Muselly.App.ViewModels;

namespace Muselly.App.Views;

public partial class UserProfileView : UserControl
{
    public UserProfileView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnChoosePicture(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not UserProfileViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a profile picture",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        var file = files?.FirstOrDefault();
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.ProfilePicturePath = path;
            vm.SaveCommand.Execute(null);
        }
    }
}
