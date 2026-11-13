using CommunityToolkit.Mvvm.ComponentModel;
using FluentTaskScheduler.Models.Enums;

namespace FluentTaskScheduler.ViewModels
{
    /// <summary>State of the create/edit task dialog.</summary>
    public partial class MainViewModel
    {
        /// <summary>Whether the open dialog creates a blank task, edits the selected one, or starts from a template.</summary>
        [ObservableProperty]
        private DialogMode _dialogMode = DialogMode.Create;
    }
}
