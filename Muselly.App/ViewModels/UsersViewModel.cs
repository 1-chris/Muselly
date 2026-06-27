using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Muselly.App.Services;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;

namespace Muselly.App.ViewModels;

/// <summary>
/// The Users directory. The built-in/admin user sees every account; other users see only those whose profile
/// is public (plus themselves). Selecting a user opens their (privacy-filtered) profile page.
/// </summary>
public sealed partial class UsersViewModel : ViewModelBase
{
    private readonly IUserService _users;
    private readonly INavigationService _nav;

    public UsersViewModel(IUserService users, INavigationService nav)
    {
        _users = users;
        _nav = nav;
        _users.Changed += (_, _) => OnUi(Reload);
        Reload();
    }

    public ObservableCollection<UserProfile> Users { get; } = new();

    public bool IsAdminView => _users.IsCurrentAdmin;

    private void Reload()
    {
        Users.Clear();
        foreach (var u in _users.VisibleTo(_users.Current)) Users.Add(u);
        OnPropertyChanged(nameof(IsAdminView));
    }

    [RelayCommand]
    private void OpenUser(UserProfile? user)
    {
        if (user is not null) _nav.ShowUserProfile(user.Username);
    }

    private static void OnUi(System.Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}
