using System;
using System.Collections.ObjectModel;
using System.Linq;
using FluentTaskScheduler.Helpers;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using FluentTaskScheduler.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentTaskScheduler.Controls
{
    /// <summary>
    /// The trigger section of the create/edit task dialog: the list of triggers with add / remove /
    /// reorder buttons, and the editor for the selected trigger (type, start time, recurrence,
    /// event / idle / session settings, random delay, expiration and the "stop if runs longer than" option).
    /// Edits are written straight to the selected <see cref="TaskTriggerModel"/>.
    /// </summary>
    public sealed partial class TriggerEditorControl : UserControl
    {
        public TriggerEditorViewModel ViewModel { get; } = new();

        // True while a trigger is being loaded into the controls (or the control is still being
        // created), so those programmatic changes aren't written back to the model.
        private bool _isPopulating = true;

        /// <summary>Raised after a trigger has been loaded into the editor, so the page can load its own per-trigger settings.</summary>
        public event EventHandler<TaskTriggerModel>? SelectedTriggerChanged;

        public TriggerEditorControl()
        {
            this.InitializeComponent();
            ApplyLocalizedText();
            _isPopulating = false;
            Loaded += (s, e) => LocalizationService.LanguageChanged += OnLanguageChanged;
            Unloaded += (s, e) => LocalizationService.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLocalizedText();

        // ── Public API ───────────────────────────────────────────────────────────────

        /// <summary>The triggers being edited; assign a new collection when the dialog opens for another task.</summary>
        public ObservableCollection<TaskTriggerModel> EditedTriggers
        {
            get => ViewModel.Triggers;
            set => ViewModel.Triggers = value;
        }

        /// <summary>Index of the selected trigger in the list, or -1.</summary>
        public int SelectedIndex
        {
            get => TriggerList.SelectedIndex;
            set => TriggerList.SelectedIndex = value;
        }

        /// <summary>The selected trigger, or null.</summary>
        public TaskTriggerModel? SelectedTrigger => TriggerList.SelectedItem as TaskTriggerModel;

        /// <summary>Whether "Stop task if runs longer than" is ticked.</summary>
        public bool IsStopAfterEnabled => EditTaskStopAfter.IsChecked == true;

        /// <summary>
        /// The duration typed or picked for "Stop task if runs longer than". It is an editable combo
        /// box: a custom value has no selected item, so it is read from the text.
        /// </summary>
        public string StopAfterValue =>
            (EditTaskStopAfterVal.SelectedItem as ComboBoxItem)?.Tag?.ToString()
            ?? EditTaskStopAfterVal.Text?.Trim() ?? "";

        /// <summary>Loads an existing task's "stop if runs longer than" value (empty or null means the option is off).</summary>
        public void SetStopAfter(string? value)
        {
            bool hasStopAfter = !string.IsNullOrWhiteSpace(value);
            EditTaskStopAfter.IsChecked = hasStopAfter;
            EditTaskStopAfterVal.IsEnabled = hasStopAfter;
            if (hasStopAfter)
            {
                bool matched = false;
                foreach (var item in EditTaskStopAfterVal.Items.Cast<ComboBoxItem>())
                    if (item.Tag?.ToString() == value) { EditTaskStopAfterVal.SelectedItem = item; matched = true; break; }
                if (!matched) EditTaskStopAfterVal.Text = value;
            }
        }

        // ── Type / visibility ────────────────────────────────────────────────────────

        /// <summary>Tells the view model which trigger type the type combo box currently shows, so it can show the matching panel.</summary>
        private void SyncTypeFromCombo()
        {
            TriggerType? type = EditTaskTriggerType.SelectedItem is ComboBoxItem item &&
                                Enum.TryParse<TriggerType>(item.Tag?.ToString(), out var parsed)
                ? parsed
                : null;
            ViewModel.ShowTrigger(type);
        }

        // ── List buttons ─────────────────────────────────────────────────────────────

        private void BtnAddTrigger_Click(object sender, RoutedEventArgs e) => TriggerList.SelectedIndex = ViewModel.AddTrigger();

        private void BtnRemoveTrigger_Click(object sender, RoutedEventArgs e) => ViewModel.RemoveTrigger(TriggerList.SelectedIndex);

        private void BtnMoveTriggerUp_Click(object sender, RoutedEventArgs e)
        {
            int idx = TriggerList.SelectedIndex;
            int moved = ViewModel.MoveUp(idx);
            if (moved != idx) TriggerList.SelectedIndex = moved;
        }

        private void BtnMoveTriggerDown_Click(object sender, RoutedEventArgs e)
        {
            int idx = TriggerList.SelectedIndex;
            int moved = ViewModel.MoveDown(idx);
            if (moved != idx) TriggerList.SelectedIndex = moved;
        }

        private void InfoIcon_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true; // Don't bubble to the dialog's ScrollViewer
            if (sender is FrameworkElement icon)
                InfoFlyoutHelper.Show(icon, VisualTreeUtil.FindParent<ScrollViewer>(icon));
        }
    }
}
