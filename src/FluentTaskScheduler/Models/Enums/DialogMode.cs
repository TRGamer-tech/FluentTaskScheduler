namespace FluentTaskScheduler.Models.Enums
{
    /// <summary>How the create/edit task dialog was opened.</summary>
    public enum DialogMode
    {
        /// <summary>A brand-new, blank task.</summary>
        Create,
        /// <summary>Editing the selected existing task.</summary>
        Edit,
        /// <summary>A new task pre-filled from a template.</summary>
        FromTemplate
    }
}
