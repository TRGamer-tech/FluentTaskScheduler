using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.ViewModels
{
    /// <summary>
    /// State of the trigger editor in the create/edit task dialog: the list of triggers being edited
    /// and which parts of the editor are visible for the selected trigger's type. Holds no UI code.
    /// </summary>
    public partial class TriggerEditorViewModel : ObservableObject
    {
        /// <summary>The triggers being edited. Replaced as a whole when the dialog is opened for another task.</summary>
        [ObservableProperty]
        private ObservableCollection<TaskTriggerModel> _triggers = new();

        /// <summary>
        /// Whether the detail editor is shown. It becomes visible when a trigger is first selected and
        /// then stays visible, so it doesn't flicker while the list selection changes.
        /// </summary>
        [ObservableProperty]
        private bool _isDetailsVisible;

        /// <summary>Type of the trigger currently shown in the editor; null before any trigger is selected.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsStartTimeVisible))]
        [NotifyPropertyChangedFor(nameof(IsDailyVisible))]
        [NotifyPropertyChangedFor(nameof(IsWeeklyVisible))]
        [NotifyPropertyChangedFor(nameof(IsMonthlyVisible))]
        [NotifyPropertyChangedFor(nameof(IsEventVisible))]
        [NotifyPropertyChangedFor(nameof(IsIdleVisible))]
        [NotifyPropertyChangedFor(nameof(IsSessionStateVisible))]
        private TriggerType? _selectedType;

        public bool IsDailyVisible => SelectedType == TriggerType.Daily;
        public bool IsWeeklyVisible => SelectedType == TriggerType.Weekly;
        public bool IsMonthlyVisible => SelectedType == TriggerType.Monthly;
        public bool IsEventVisible => SelectedType == TriggerType.Event;
        public bool IsIdleVisible => SelectedType == TriggerType.OnIdle;
        public bool IsSessionStateVisible => SelectedType == TriggerType.SessionStateChange;

        /// <summary>The start date/time pickers apply to every type except those fired by an event, idleness or a session change.</summary>
        public bool IsStartTimeVisible =>
            SelectedType is not (TriggerType.Event or TriggerType.OnIdle or TriggerType.SessionStateChange);

        /// <summary>Called when a trigger becomes selected: shows the editor for that trigger's type.</summary>
        public void ShowTrigger(TriggerType? type)
        {
            IsDetailsVisible = true;
            SelectedType = type;
        }

        /// <summary>Appends a new daily trigger starting now and returns its index.</summary>
        public int AddTrigger()
        {
            Triggers.Add(new TaskTriggerModel
            {
                TriggerType = TriggerType.Daily,
                ScheduleInfo = DurationUtil.FormatScheduleInfo(DateTime.Now)
            });
            return Triggers.Count - 1;
        }

        /// <summary>Removes the trigger at <paramref name="index"/>, if there is one.</summary>
        public void RemoveTrigger(int index)
        {
            if (index >= 0 && index < Triggers.Count) Triggers.RemoveAt(index);
        }

        /// <summary>Moves the trigger one place up and returns its new index (unchanged if it can't move).</summary>
        public int MoveUp(int index)
        {
            if (index <= 0 || index >= Triggers.Count) return index;
            var item = Triggers[index];
            Triggers.RemoveAt(index);
            Triggers.Insert(index - 1, item);
            return index - 1;
        }

        /// <summary>Moves the trigger one place down and returns its new index (unchanged if it can't move).</summary>
        public int MoveDown(int index)
        {
            if (index < 0 || index >= Triggers.Count - 1) return index;
            var item = Triggers[index];
            Triggers.RemoveAt(index);
            Triggers.Insert(index + 1, item);
            return index + 1;
        }
    }
}
