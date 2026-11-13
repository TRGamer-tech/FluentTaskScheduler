using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler.ViewModels
{
    /// <summary>Sort state of the task list and the text derived from it.</summary>
    public partial class MainViewModel
    {
        /// <summary>Text of the toolbar sort button: a neutral label, or the active column with its direction.</summary>
        public string SortButtonText
        {
            get
            {
                if (SortColumn == SortColumn.None)
                    return LocalizationService.GetString("Main.Toolbar.SortButton", "Sort ↕");
                string arrow = SortAscending ? "▲" : "▼";
                return string.Format(LocalizationService.GetString("Main.Toolbar.SortActiveFormat", "Sort {0} {1}"), arrow, SortColumn);
            }
        }

        /// <summary>Direction marker shown next to a column in the sort menu; empty unless that column is the active sort.</summary>
        public string GetSortIndicator(SortColumn column) =>
            SortColumn == column ? (SortAscending ? " ▲" : " ▼") : "";
    }
}
