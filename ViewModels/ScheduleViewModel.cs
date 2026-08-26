using System;
using System.Collections.ObjectModel;
using System.Linq;
using BackupManager.Models;
using BackupManager.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupManager.ViewModels;

public partial class ScheduleViewModel : ViewModelBase, IRefreshable
{
    private readonly IConfigStore _config;
    private readonly ISchedulerService _scheduler;
    private readonly INotificationService _notify;

    [ObservableProperty]
    private ObservableCollection<BackupJob> _jobs = new();

    [ObservableProperty]
    private ObservableCollection<RepositoryConfig> _repositories = new();

    [ObservableProperty]
    private BackupJob? _selected;

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
    private bool _enabled = true;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private bool _schedulePaused;

    public ScheduleViewModel(IConfigStore config, ISchedulerService scheduler, INotificationService notify)
    {
        _config = config;
        _scheduler = scheduler;
        _notify = notify;
    }

    public void Refresh()
    {
        Repositories = new ObservableCollection<RepositoryConfig>(_config.Config.Repositories);
        Jobs = new ObservableCollection<BackupJob>(_config.Config.Jobs);
        SchedulePaused = _scheduler.IsPaused;
        Status = "";
    }

    partial void OnSelectedChanged(BackupJob? value)
    {
        if (value is null)
        {
            JobName = ""; LocalPathPreview = ""; ExcludesText = ""; TagsText = "";
            ScheduleTime = "08:30"; Enabled = true; RepositoryId = "";
            return;
        }
        JobName = value.Name;
        RepositoryId = value.RepositoryId;
        LocalPathPreview = Repositories.FirstOrDefault(r => r.Id == value.RepositoryId)?.LocalPath ?? "";
        ExcludesText = string.Join("\n", value.Excludes);
        TagsText = string.Join(", ", value.Tags);
        ScheduleTime = value.ScheduleTime.ToString(@"hh\:mm");
        Enabled = value.Enabled;
    }

    partial void OnRepositoryIdChanged(string value)
    {
        LocalPathPreview = Repositories.FirstOrDefault(r => r.Id == value)?.LocalPath ?? "";
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        JobName = "Daily Backup";
        RepositoryId = Repositories.FirstOrDefault()?.Id ?? "";
        LocalPathPreview = Repositories.FirstOrDefault()?.LocalPath ?? "";
        ExcludesText = "";
        TagsText = "";
        ScheduleTime = "08:30";
        Enabled = true;
        Status = "";
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(JobName))
        {
            Status = "Job name is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(RepositoryId))
        {
            Status = "Choose a repository.";
            return;
        }
        var repo = Repositories.FirstOrDefault(r => r.Id == RepositoryId);
        if (repo is null || string.IsNullOrWhiteSpace(repo.LocalPath))
        {
            Status = "Set the local copy folder on the repository first.";
            return;
        }
        if (!TimeSpan.TryParse(ScheduleTime, out var time))
        {
            Status = "Schedule time must be HH:mm (e.g. 08:30).";
            return;
        }

        var job = Selected ?? new BackupJob();
        job.Name = JobName;
        job.RepositoryId = RepositoryId;
        job.Excludes = ExcludesText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        job.Tags = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        job.ScheduleTime = time;
        job.Enabled = Enabled;

        if (Selected is null)
        {
            Jobs.Add(job);
            _config.Config.Jobs.Add(job);
            Selected = job;
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
        Status = "Saved.";
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null)
            return;
        Jobs.Remove(Selected);
        _config.Config.Jobs.RemoveAll(j => j.Id == Selected.Id);
        _config.Save();
        Selected = null;
        Status = "Job removed.";
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (Selected is null)
        {
            Status = "Select or create a job first.";
            return;
        }
        Busy = true;
        Status = $"Running {Selected.Name}…";
        try
        {
            await _scheduler.RunJobAsync(Selected);
            Status = "Done.";
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        SchedulePaused = !SchedulePaused;
        _scheduler.IsPaused = SchedulePaused;
    }
}
