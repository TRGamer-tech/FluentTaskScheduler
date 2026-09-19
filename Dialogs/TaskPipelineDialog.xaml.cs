using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentTaskScheduler.Dialogs
{
    /// <summary>Row in the task pickers: shows the friendly name, carries the full path.</summary>
    public class PipelineTaskChoice
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayName => string.IsNullOrEmpty(Path) || Path == "\\" + Name
            ? Name
            : $"{Name}  ({Path})";
    }

    /// <summary>A configured downstream target, shown as name + full path in the branch lists.</summary>
    public class PipelineTargetRow
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// Editor for a task's completion actions ("execution pipeline"): which tasks run after this
    /// one finishes, split by exit code 0 vs non-zero.
    /// </summary>
    public sealed partial class TaskPipelineDialog : ContentDialog
    {
        private readonly string _ownTaskPath;
        private readonly ObservableCollection<PipelineTargetRow> _successTargets = new();
        private readonly ObservableCollection<PipelineTargetRow> _failureTargets = new();
        private readonly Dictionary<string, string> _nameByPath = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The edited pipeline. Only meaningful once the dialog returned Primary.</summary>
        public TaskPipeline Result { get; private set; } = new();

        private static string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

        /// <param name="ownTaskPath">Path of the task being edited, excluded from the pickers to avoid self-chaining.</param>
        /// <param name="current">Existing configuration, or null for a fresh one.</param>
        /// <param name="availableTasks">Every task the user can chain to.</param>
        public TaskPipelineDialog(string ownTaskPath, TaskPipeline? current, IEnumerable<ScheduledTaskModel> availableTasks)
        {
            this.InitializeComponent();
            _ownTaskPath = ownTaskPath ?? "";
            this.RequestedTheme = SettingsService.Theme;

            var choices = (availableTasks ?? Enumerable.Empty<ScheduledTaskModel>())
                .Where(t => !string.IsNullOrWhiteSpace(t.Path))
                .Where(t => !string.Equals(t.Path, _ownTaskPath, StringComparison.OrdinalIgnoreCase))
                .Where(t => !t.IsReadOnlyFallback)
                .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(t => new PipelineTaskChoice { Path = t.Path, Name = t.Name })
                .ToList();

            foreach (var c in choices) _nameByPath[c.Path] = c.Name;

            var existing = current?.Clone() ?? new TaskPipeline();
            foreach (var p in existing.OnSuccessTasks) _successTargets.Add(ToRow(p));
            foreach (var p in existing.OnFailureTasks) _failureTargets.Add(ToRow(p));

            SuccessList.ItemsSource = _successTargets;
            FailureList.ItemsSource = _failureTargets;
            PipelineEnabledSwitch.IsOn = existing.IsEnabled;

            SuccessTaskPicker.ItemsSource = choices;
            FailureTaskPicker.ItemsSource = new List<PipelineTaskChoice>(choices);

            ApplyLocalizedUi();
            UpdateBodyState();

            if (choices.Count == 0)
            {
                ShowWarning(L("Pipeline.Warning.NoTasks",
                    "No other tasks are available to chain to. Create another task first."));
            }
        }

        /// <summary>Resolves a stored path to a display row; unknown paths still show their file name.</summary>
        private PipelineTargetRow ToRow(string path) => new()
        {
            Path = path,
            Name = _nameByPath.TryGetValue(path, out var name) ? name : System.IO.Path.GetFileName(path)
        };

        private void ApplyLocalizedUi()
        {
            Title = L("Pipeline.Dialog.Title", "Completion Actions");
            PrimaryButtonText = L("Dialog.Common.Save", "Save");
            CloseButtonText = L("Dialog.Common.Cancel", "Cancel");

            PipelineInfoBar.Title = L("Pipeline.Info.Title", "How chaining works");
            PipelineInfoBar.Message = L("Pipeline.Info.Message",
                "When this task finishes, FluentTaskScheduler reads the action's exit code from the Windows Task Scheduler event log. " +
                "Exit code 0 starts the success tasks; any other code starts the failure tasks. " +
                "The app must be running for chained tasks to be started, and chains are skipped while Global Snooze is active.");

            EnableTitle.Text = L("Pipeline.EnableSwitch", "Run downstream tasks when this task completes");
            EnableSubtitle.Text = L("Pipeline.EnableSubtitle",
                "Turn this off to keep the configuration without acting on it.");

            OnSuccessHeader.Text = L("Pipeline.SuccessHeader", "On success");
            OnSuccessHint.Text = L("Pipeline.SuccessHint", "Started when the action exits with code 0.");
            OnFailureHeader.Text = L("Pipeline.FailureHeader", "On failure");
            OnFailureHint.Text = L("Pipeline.FailureHint", "Started on any non-zero exit code, or if the action could not launch.");

            ToolTipService.SetToolTip(AddSuccessButton, L("Pipeline.Add", "Add"));
            ToolTipService.SetToolTip(AddFailureButton, L("Pipeline.Add", "Add"));
            RemoveSuccessText.Text = L("Pipeline.Remove", "Remove selected");
            RemoveFailureText.Text = L("Pipeline.Remove", "Remove selected");

            SuccessTaskPicker.PlaceholderText = L("Pipeline.PickTask", "Choose a task...");
            FailureTaskPicker.PlaceholderText = L("Pipeline.PickTask", "Choose a task...");
        }

        private void ShowWarning(string message)
        {
            PipelineWarningBar.Message = message;
            PipelineWarningBar.IsOpen = true;
        }

        private void PipelineEnabled_Toggled(object sender, RoutedEventArgs e) => UpdateBodyState();

        private void UpdateBodyState()
        {
            // The lists stay visible when disabled so the user can still see what is configured.
            if (PipelineBodyPanel != null)
                PipelineBodyPanel.Opacity = PipelineEnabledSwitch.IsOn ? 1.0 : 0.5;
        }

        private void AddSuccess_Click(object sender, RoutedEventArgs e) =>
            AddChoice(SuccessTaskPicker, _successTargets, _failureTargets);

        private void AddFailure_Click(object sender, RoutedEventArgs e) =>
            AddChoice(FailureTaskPicker, _failureTargets, _successTargets);

        private void AddChoice(ComboBox picker, ObservableCollection<PipelineTargetRow> target,
                               ObservableCollection<PipelineTargetRow> other)
        {
            if (picker.SelectedItem is not PipelineTaskChoice choice) return;

            if (target.Any(r => string.Equals(r.Path, choice.Path, StringComparison.OrdinalIgnoreCase)))
            {
                ShowWarning(string.Format(L("Pipeline.Warning.Duplicate", "'{0}' is already in this list."), choice.Name));
                return;
            }

            target.Add(new PipelineTargetRow { Path = choice.Path, Name = choice.Name });
            PipelineWarningBar.IsOpen = false;
            picker.SelectedItem = null;

            if (other.Any(r => string.Equals(r.Path, choice.Path, StringComparison.OrdinalIgnoreCase)))
            {
                ShowWarning(string.Format(
                    L("Pipeline.Warning.BothLists", "'{0}' now runs on both success and failure — it will start after every completion."),
                    choice.Name));
            }
        }

        private void RemoveSuccess_Click(object sender, RoutedEventArgs e)
        {
            if (SuccessList.SelectedItem is PipelineTargetRow row) _successTargets.Remove(row);
        }

        private void RemoveFailure_Click(object sender, RoutedEventArgs e)
        {
            if (FailureList.SelectedItem is PipelineTargetRow row) _failureTargets.Remove(row);
        }

        private void Dialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            Result = new TaskPipeline
            {
                IsEnabled = PipelineEnabledSwitch.IsOn,
                OnSuccessTasks = _successTargets.Select(r => r.Path).ToList(),
                OnFailureTasks = _failureTargets.Select(r => r.Path).ToList()
            };

            if (Result.IsEnabled && !Result.HasAnyTargets)
            {
                args.Cancel = true;
                ShowWarning(L("Pipeline.Warning.NoTargets",
                    "Add at least one downstream task, or turn the switch off."));
                return;
            }

            LogService.Info(
                $"Pipeline saved for '{_ownTaskPath}': enabled={Result.IsEnabled}, " +
                $"{Result.OnSuccessTasks.Count} on success, {Result.OnFailureTasks.Count} on failure.");
        }
    }
}
