using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Services;
using Microsoft.UI.Xaml;

namespace FluentTaskScheduler.ViewModels
{
    /// <summary>A template card as shown in the library grid.</summary>
    public class TaskTemplateCard
    {
        public TaskTemplate Template { get; set; } = new();

        public string Name => Template.Name;
        public string Description => Template.Description;
        public string Glyph => string.IsNullOrEmpty(Template.Glyph) ? "" : Template.Glyph;
        public string Note => Template.Note;
        public Visibility NoteVisibility => Template.HasNote ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AdminVisibility => Template.RunAsAdmin ? Visibility.Visible : Visibility.Collapsed;
        public string DeployLabel => LocalizationService.GetString("Templates.Deploy", "Deploy Template");

        /// <summary>"powershell.exe · Daily 03:00" style summary line.</summary>
        public string Summary
        {
            get
            {
                string command = System.IO.Path.GetFileName(Template.Command);
                string trigger = Template.TriggerType switch
                {
                    "AtLogon" => LocalizationService.GetString("Trigger.AtLogon", "At Logon"),
                    "AtStartup" => LocalizationService.GetString("Trigger.AtStartup", "At Startup"),
                    "OnIdle" => LocalizationService.GetString("Trigger.OnIdle", "On Idle"),
                    "Weekly" => $"{LocalizationService.GetString("Dialog.Trigger.Weekly", "Weekly")} " +
                                $"{string.Join(", ", Template.WeeklyDays)} {Template.TriggerHour:00}:{Template.TriggerMinute:00}",
                    _ => $"{LocalizationService.GetString("Dialog.Trigger.Daily", "Daily")} " +
                         $"{Template.TriggerHour:00}:{Template.TriggerMinute:00}"
                };
                return $"{command}  ·  {trigger}";
            }
        }
    }

    /// <summary>A named group of template cards, rendered as one section of the library.</summary>
    public class TaskTemplateGroup
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public ObservableCollection<TaskTemplateCard> Templates { get; } = new();
    }

    public class TaskTemplatesViewModel
    {
        private List<TaskTemplate> _all = new();
        private string _currentFilter = "";

        /// <summary>The groups matching the current search filter — this is what the UI binds to.</summary>
        public ObservableCollection<TaskTemplateGroup> Groups { get; } = new();

        /// <summary>Reloads the full template library and re-applies the current filter. Called on load and whenever the language changes.</summary>
        public void Load()
        {
            _all = TaskTemplateLibrary.GetAll();
            ApplyFilter(_currentFilter);
        }

        /// <summary>Filters by name or description; groups with no remaining matches are hidden entirely. Empty/null clears the filter.</summary>
        public void ApplyFilter(string? query)
        {
            _currentFilter = query ?? "";

            IEnumerable<TaskTemplate> source = _all;
            if (!string.IsNullOrWhiteSpace(_currentFilter))
            {
                source = _all.Where(t =>
                    (t.Name != null && t.Name.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase)) ||
                    (t.Description != null && t.Description.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase)));
            }
            var filtered = source.ToList();

            Groups.Clear();
            foreach (var key in TaskTemplateLibrary.Groups)
            {
                var group = new TaskTemplateGroup
                {
                    Key = key,
                    DisplayName = TaskTemplateLibrary.GetGroupDisplayName(key)
                };

                foreach (var template in filtered.Where(t => string.Equals(t.Group, key, StringComparison.Ordinal)))
                    group.Templates.Add(new TaskTemplateCard { Template = template });

                if (group.Templates.Count > 0) Groups.Add(group);
            }
        }
    }
}
