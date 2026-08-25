using System.Collections.ObjectModel;
using System.Linq;
using BackupManager.Models;
using BackupManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupManager.ViewModels;

public partial class RepositoriesViewModel : ViewModelBase, IRefreshable
{
    private readonly IConfigStore _config;
    private readonly IResticService _restic;
    private readonly INotificationService _notify;

    [ObservableProperty]
    private ObservableCollection<RepositoryConfig> _repositories = new();

    [ObservableProperty]
    private RepositoryConfig? _selected;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _location = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _busy;

    public RepositoriesViewModel(IConfigStore config, IResticService restic, INotificationService notify)
    {
        _config = config;
        _restic = restic;
        _notify = notify;
    }

    public void Refresh()
    {
        Repositories = new ObservableCollection<RepositoryConfig>(_config.Config.Repositories);
        Status = "";
    }

    partial void OnSelectedChanged(RepositoryConfig? value)
    {
        if (value is null)
        {
            Name = Location = Password = Notes = "";
            return;
        }
        Name = value.Name;
        Location = value.Location;
        Password = value.Password;
        Notes = value.Notes ?? "";
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        Name = "New Repository";
        Location = "";
        Password = "";
        Notes = "";
        Status = "";
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Location))
        {
            Status = "Name and location are required.";
            return;
        }
        var repo = Selected ?? new RepositoryConfig();
        repo.Name = Name;
        repo.Location = Location;
        repo.Password = Password;
        repo.Notes = Notes;
        if (Selected is null)
        {
            Repositories.Add(repo);
            _config.Config.Repositories.Add(repo);
            Selected = repo;
        }
        else
        {
            var existing = _config.Config.Repositories.FirstOrDefault(r => r.Id == repo.Id);
            if (existing is not null)
            {
                existing.Name = repo.Name;
                existing.Location = repo.Location;
                existing.Password = repo.Password;
                existing.Notes = repo.Notes;
            }
        }
        _config.Save();
        Status = "Saved.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null)
            return;
        Repositories.Remove(Selected);
        _config.Config.Repositories.RemoveAll(r => r.Id == Selected.Id);
        _config.Config.Jobs.RemoveAll(j => j.RepositoryId == Selected.Id);
        _config.Save();
        Selected = null;
        Status = "Repository removed.";
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (Selected is null)
            return;
        Busy = true;
        Status = "Testing connection…";
        try
        {
            var ok = await _restic.IsRepositoryInitializedAsync(Selected);
            Status = ok ? "Connection OK (repository exists)." : "Repository not found. Use Init to create it.";
        }
        catch (System.Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private async Task InitAsync()
    {
        if (Selected is null)
            return;
        Busy = true;
        Status = "Initializing repository…";
        try
        {
            await _restic.InitRepositoryAsync(Selected);
            _config.Save();
            Status = "Repository initialized.";
            _notify.Show("Repository ready", $"{Selected.Name} is initialized.", false);
        }
        catch (System.Exception ex)
        {
            Status = "Init failed: " + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }
}
