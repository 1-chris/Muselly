using System.Threading.Tasks;

namespace Muselly.App.Services;

/// <summary>
/// Opens the OS-native directory chooser and returns the selected folder's local path (or <c>null</c> if
/// cancelled / unavailable). Backed by Avalonia's cross-platform StorageProvider, so it uses the real
/// native picker on Windows, macOS and Linux.
/// </summary>
public interface IFolderPicker
{
    Task<string?> PickFolderAsync(string title = "Select a music folder");
}
