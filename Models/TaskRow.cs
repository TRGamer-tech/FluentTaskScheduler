using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Models.Enums;

namespace FluentTaskScheduler.Models
{
    /// <summary>One task as the task list shows it.</summary>
    public sealed partial class TaskRow : ObservableObject
    {
        public TaskRow(string path)
        {
            Path = path;
            int separator = path.LastIndexOf('\\');
            Name = path[(separator + 1)..];
            FolderPath = separator <= 0 ? "\\" : path[..separator];
        }

        /// <summary>Full path in Task Scheduler, e.g. \Backups\Nightly. Identifies the task.</summary>
        public string Path { get; }
        public string Name { get; }
        /// <summary>Folder the task lives in; "\" for the root.</summary>
        public string FolderPath { get; }

        [ObservableProperty] private TaskState _state = TaskState.Unknown;
        [ObservableProperty] private bool _isEnabled;

        /// <summary>True from the moment the task is started here until it is seen to have finished, so the row can show progress without waiting for a refresh.</summary>
        [ObservableProperty] private bool _isRunning;

        [ObservableProperty] private bool _isSelected;
        [ObservableProperty] private bool _isHidden;
        [ObservableProperty] private string _description = "";
        [ObservableProperty] private string _author = "";
        [ObservableProperty] private string _category = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(TagsText))]
        private IReadOnlyList<string> _tags = Array.Empty<string>();

        [ObservableProperty] private DateTime? _lastRunTime;
        [ObservableProperty] private DateTime? _nextRunTime;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasPipeline))]
        private TaskPipeline _pipeline = new();

        /// <summary>
        /// True for a task that is known to exist (it appears in the Task Scheduler event log) but
        /// that this user is not allowed to read. It is listed so it does not silently go missing;
        /// nothing can be done with it.
        /// </summary>
        [ObservableProperty] private bool _isInaccessible;

        /// <summary>Why the task's definition could not be read, if it could not. The task is still listed.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasLoadError))]
        private string? _loadError;

        public string TagsText => string.Join(", ", Tags);
        public bool HasPipeline => Pipeline.IsActive;
        public bool HasLoadError => LoadError is not null;

        /// <summary>Takes over the state of a freshly read row for the same task. Selection and the running marker are kept.</summary>
        public void UpdateFrom(TaskRow fresh)
        {
            State = fresh.State;
            IsEnabled = fresh.IsEnabled;
            IsHidden = fresh.IsHidden;
            Description = fresh.Description;
            Author = fresh.Author;
            Category = fresh.Category;
            if (!SameItems(Tags, fresh.Tags)) Tags = fresh.Tags;
            LastRunTime = fresh.LastRunTime;
            NextRunTime = fresh.NextRunTime;
            Pipeline = fresh.Pipeline;
            IsInaccessible = fresh.IsInaccessible;
            LoadError = fresh.LoadError;
        }

        private static bool SameItems(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
            return true;
        }
    }
}
