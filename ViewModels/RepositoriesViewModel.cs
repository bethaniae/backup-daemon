using System;
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
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notify;

    // Repositories

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

    // Jobs

    [ObservableProperty]
    private ObservableCollection<BackupJob> _jobs = new();

    [ObservableProperty]
    private BackupJob? _selectedJob;

    [ObservableProperty]
    private string _jobName = "";

    [ObservableProperty]
    private string _repositoryId = "";

    [ObservableProperty]
    private string _localPathPreview = "";

    [ObservableProperty]
    private string _excludesText = "";

    [ObservableProperty]
    private string _tagsText = "";

    [ObservableProperty]
    private string _scheduleTime = "08:30";

    [ObservableProperty]
    private bool _jobEnabled = true;

    [ObservableProperty]
    private string _jobStatus = "";

    [ObservableProperty]
    private bool _jobBusy;

    public RepositoriesViewModel(IConfigStore config, IResticService restic,
        ISchedulerService scheduler, INotificationService notify)
    {
        _config = config;
        _restic = restic;
        _scheduler = scheduler;
        _notify = notify;
    }

    public void Refresh()
    {
        Repositories = new ObservableCollection<RepositoryConfig>(_config.Config.Repositories);
        if (Repositories.Count > 0)
            Selected = Repositories.First();
        Status = "";

        Jobs = new ObservableCollection<BackupJob>(_config.Config.Jobs);
        if (Jobs.Count > 0)
            SelectedJob = Jobs.First();
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
        Name = "New Source";
        LocalPath = "";
        Location = "";
        Password = "";
        Notes = "";
        Status = "";
    }

    partial void OnSelectedJobChanged(BackupJob? value)
    {
        if (value is null)
        {
            JobName = ""; RepositoryId = ""; LocalPathPreview = ""; ExcludesText = ""; TagsText = "";
            ScheduleTime = "08:30"; JobEnabled = true;
            return;
        }
        JobName = value.Name;
        RepositoryId = value.RepositoryId;
        LocalPathPreview = Repositories.FirstOrDefault(r => r.Id == value.RepositoryId)?.LocalPath ?? "";
        ExcludesText = string.Join("\n", value.Excludes);
        TagsText = string.Join(", ", value.Tags);
        ScheduleTime = value.ScheduleTime.ToString(@"hh\:mm");
        JobEnabled = value.Enabled;
    }

    partial void OnRepositoryIdChanged(string value)
    {
        LocalPathPreview = Repositories.FirstOrDefault(r => r.Id == value)?.LocalPath ?? "";
    }

    [RelayCommand]
    private void NewJob()
    {
        SelectedJob = null;
        JobName = "Daily Backup";
        RepositoryId = Repositories.FirstOrDefault()?.Id ?? "";
        LocalPathPreview = Repositories.FirstOrDefault()?.LocalPath ?? "";
        ExcludesText = "";
        TagsText = "";
        ScheduleTime = "08:30";
        JobEnabled = true;
        JobStatus = "";
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
            Status = "Name and source location are required.";
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
        if (Selected is not { } repo)
            return;

        var id = repo.Id;
        Selected = null;
        Repositories.Remove(repo);
        _config.Config.Repositories.RemoveAll(r => r is null || r.Id == id);
        _config.Config.Jobs.RemoveAll(j => j is null || j.RepositoryId == id);
        _config.Save();
        Status = "Source removed.";
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
            Status = ok ? "Connection OK (source exists)." : "Source not found. Use Init to create it.";
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
        Status = "Initializing remote source… (your local copy is not touched)";
        try
        {
            var result = await _restic.InitRepositoryAsync(Selected);
            _config.Save();
            Status = result.Message;
            if (result.Success)
                _notify.Show("Source", $"{Selected.Name}: {result.Message}", false);
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

    [RelayCommand]
    private void SaveJob()
    {
        if (string.IsNullOrWhiteSpace(JobName))
        {
            JobStatus = "Job name is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RepositoryId))
        {
            JobStatus = "Choose a source.";
            return;
        }
        var repo = Repositories.FirstOrDefault(r => r.Id == RepositoryId);
        if (repo is null || string.IsNullOrWhiteSpace(repo.LocalPath))
        {
            JobStatus = "Set the local copy folder on the source first.";
            return;
        }
        if (!TimeSpan.TryParse(ScheduleTime, out var time))
        {
            JobStatus = "Schedule time must be HH:mm (e.g. 08:30).";
            return;
        }

        var job = SelectedJob ?? new BackupJob();
        job.Name = JobName;
        job.RepositoryId = RepositoryId;
        job.Excludes = ExcludesText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        job.Tags = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        job.ScheduleTime = time;
        job.Enabled = JobEnabled;

        if (SelectedJob is null)
        {
            Jobs.Add(job);
            _config.Config.Jobs.Add(job);
            SelectedJob = job;
        }
        else
        {
            var existing = _config.Config.Jobs.FirstOrDefault(j => j.Id == job.Id);
            if (existing is not null)
            {
                existing.Name = job.Name;
                existing.RepositoryId = job.RepositoryId;
                existing.Excludes = job.Excludes;
                existing.Tags = job.Tags;
                existing.ScheduleTime = job.ScheduleTime;
                existing.Enabled = job.Enabled;
            }
        }
        _config.Save();
        JobStatus = "Saved.";
    }

    [RelayCommand]
    private void DeleteJob()
    {
        if (SelectedJob is null)
            return;
        var id = SelectedJob.Id;
        var toRemove = SelectedJob;
        SelectedJob = null;
        Jobs.Remove(toRemove);
        _config.Config.Jobs.RemoveAll(j => j is null || j.Id == id);
        _config.Save();
        JobStatus = "Job removed.";
    }

    [RelayCommand]
    private async Task RunJobNowAsync()
    {
        if (SelectedJob is null)
        {
            JobStatus = "Select or create a job first.";
            return;
        }
        JobBusy = true;
        JobStatus = $"Running {SelectedJob.Name}…";
        try
        {
            await _scheduler.RunJobAsync(SelectedJob);
            JobStatus = "Done.";
        }
        finally
        {
            JobBusy = false;
        }
    }
}
