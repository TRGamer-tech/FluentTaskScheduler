namespace FluentTaskScheduler.ViewModels;

/// <summary>The running app as a whole.</summary>
public interface IAppLifetime
{
    /// <summary>True when the app runs with administrator rights.</summary>
    bool IsElevated { get; }

    /// <summary>Closes all windows and ends the app the regular way, so everything pending is saved.</summary>
    void Exit();

    /// <summary>
    /// Starts the app again with administrator rights and ends this instance. Windows asks the
    /// user for consent first.
    /// </summary>
    /// <returns>False when the user declined; this instance then keeps running.</returns>
    bool RestartElevated();
}
