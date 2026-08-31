using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Platform.Storage;
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
    private string _localPath = "";

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
            Name = LocalPath = Location = Password = Notes = "";
            return;
        }
        Name = value.Name;
        LocalPath = value.LocalPath;
        Location = value.Location;
        Password = value.Password;
        Notes = value.Notes ?? "";
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        Name = "New Repository";
        LocalPath = "";
        Location = "";
        Password = "";
        Notes = "";
        Status = "";
    }

    [RelayCommand]
    private async Task PickLocalFolderAsync()
    {
        var window = App.MainWindowRef;
        if (window is null)
            return;
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the local copy folder to sync",
            AllowMultiple = false
        });
        if (folders.Count > 0)
            LocalPath = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Location))
        {
            Status = "Name and repository location are required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(LocalPath))
        {
            Status = "Set the local copy folder (the folder to keep synced).";
            return;
        }
        var repo = Selected ?? new RepositoryConfig();
        repo.Name = Name;
        repo.LocalPath = LocalPath;
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
        
        Console.WriteLine(_config.Config.Repositories.Count);
        Console.WriteLine(_config.Config.Jobs.Count);
        _config.Config.Repositories.ForEach((r) => { Console.WriteLine(r); });
        _config.Config.Jobs.ForEach((r) => { Console.WriteLine(r); });

        _config.Config.Repositories.RemoveAll(r => r is null || r.Id == Selected.Id);
        _config.Config.Jobs.RemoveAll(j => j is null || j.RepositoryId == Selected.Id);
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
        Status = "Initializing remote repository… (your local copy is not touched)";
        try
        {
            var result = await _restic.InitRepositoryAsync(Selected);
            _config.Save();
            Status = result.Message;
            if (result.Success)
                _notify.Show("Repository", $"{Selected.Name}: {result.Message}", false);
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
