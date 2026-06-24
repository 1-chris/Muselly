using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Muselly.App.Services;

/// <summary>
/// <see cref="IFolderPicker"/> implemented on Avalonia's <see cref="IStorageProvider"/>. It resolves the
/// active <see cref="TopLevel"/> from the running application lifetime (desktop window or single-view root)
/// so it needs no extra wiring, and invokes the platform's native folder dialog.
/// </summary>
public sealed class StorageFolderPicker : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title = "Select a music folder")
    {
        var top = GetTopLevel();
        if (top?.StorageProvider is not { CanPickFolder: true } provider)
            return null;

        var result = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        if (result.Count == 0) return null;
        var folder = result[0];
        return folder.TryGetLocalPath() ?? folder.Path.LocalPath;
    }

    private static TopLevel? GetTopLevel()
    {
        return Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop => desktop.MainWindow,
            ISingleViewApplicationLifetime singleView when singleView.MainView is { } view => TopLevel.GetTopLevel(view),
            _ => null
        };
    }
}
