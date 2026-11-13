using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using Windows.UI;

namespace FluentTaskScheduler.Dialogs
{
    public sealed partial class OnboardingDialog : ContentDialog
    {
        private static string L(string key, string fallback) => Services.LocalizationService.GetString(key, fallback);
        private ISettingsService Settings => App.Container.GetRequiredService<ISettingsService>();

        // ── Step Definitions ─────────────────────────────────────────────────────
        private readonly struct Step
        {
            public string Icon      { get; init; }
            public string Title     { get; init; }
            public string Body      { get; init; }
            public bool ShowHint    { get; init; }   // "find this again in Settings" hint
            public bool ShowAdminWarn { get; init; } // admin-rights warning
        }

        // Built per-instance (not static) so replaying onboarding after a language switch shows the
        // current language instead of whatever was active the first time this type was loaded (2.5).
        private readonly Step[] _steps;

        private static Step[] BuildSteps() => new[]
        {
            new Step
            {
                Icon          = "\uE787",   // Calendar / Scheduler
                Title         = L("Onboarding.Step1.Title", "Welcome to FluentTaskScheduler"),
                Body          = L("Onboarding.Step1.Body", "Manage Windows Task Scheduler with a modern, fluent interface — no XML, no fuss."),
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE710",   // Add / Plus
                Title         = L("Onboarding.Step2.Title", "Create your first task"),
                Body          = L("Onboarding.Step2.Body", "Hit + New Task to schedule any program, script, or command to run automatically — daily, on login, on an event, and more."),
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE8B7",   // Folder
                Title         = L("Onboarding.Step3.Title", "Organise with folders"),
                Body          = L("Onboarding.Step3.Body", "Group related tasks into folders using the sidebar, just like Windows Explorer. Your last folder is remembered across restarts."),
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE9D2",   // Chart / History
                Title         = L("Onboarding.Step4.Title", "Track history & status"),
                Body          = L("Onboarding.Step4.Body", "Click any task to see its run history, success and failure counts, and live running status — all in one place."),
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE895",   // Sync / Updates
                Title         = L("Onboarding.Step5.Title", "Always up to date"),
                Body          = L("Onboarding.Step5.Body", "FluentTaskScheduler checks for updates automatically every time it starts. You can also trigger a manual check at any time from Settings → About → Check for Updates."),
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE773",   // Search
                Title         = L("Onboarding.Step6.Title", "Discover existing tasks"),
                Body          = L("Onboarding.Step6.Body", "Use Task Discovery to scan the Windows Event Log and automatically import tasks created by other applications — no manual recreation needed."),
                ShowHint      = false,
                ShowAdminWarn = true        // admin required for Event Log access
            },
            new Step
            {
                Icon          = "\uEF90",   // Chain / flow
                Title         = L("Onboarding.StepPipelines.Title", "Chain tasks and pause everything"),
                Body          = L("Onboarding.StepPipelines.Body", "Set Completion Actions on a task to start other tasks when it succeeds or fails. Need a break? Snooze All Tasks pauses runs \u2014 for a fixed time, until reboot, or until you resume it."),
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE9F9",   // Analytics
                Title         = L("Onboarding.StepDashboard.Title", "Analyse and deploy faster"),
                Body          = L("Onboarding.StepDashboard.Body", "The Dashboard shows an execution heatmap, health score, and live run log. The Library has ready-made task templates and reusable scripts you can deploy in one click."),
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE713",   // Settings
                Title         = L("Onboarding.Step7.Title", "Tune it to your liking"),
                Body          = L("Onboarding.Step7.Body", "Head to Settings to enable Mica backdrop, minimise to tray, configure logging, run on startup, and more."),
                ShowHint      = true,       // last slide gets the Settings hint
                ShowAdminWarn = false
            }
        };

        // ── State ────────────────────────────────────────────────────────────────
        private int _currentStep = 0;
        private Ellipse[] _dots = System.Array.Empty<Ellipse>();

        // ── Constructor ──────────────────────────────────────────────────────────
        public OnboardingDialog()
        {
            this.InitializeComponent();
            _steps = BuildSteps();
            BuildDots();
            UpdateStep();

            // Mark onboarding as seen on ANY close path (Next-through-to-"Get Started", ESC, light
            // dismiss) — previously only reaching the final step and clicking through set the flag,
            // so dismissing any other way reopened the dialog on every launch (2.5).
            this.Closed += (s, e) => Settings.HasCompletedOnboarding = true;
        }

        // ── Dot indicators ───────────────────────────────────────────────────────
        private void BuildDots()
        {
            _dots = new Ellipse[_steps.Length];
            for (int i = 0; i < _steps.Length; i++)
            {
                var dot = new Ellipse
                {
                    Width  = 8,
                    Height = 8
                };
                _dots[i] = dot;
                DotsPanel.Children.Add(dot);
            }
        }

        // ── Step renderer ────────────────────────────────────────────────────────
        private void UpdateStep()
        {
            var step = _steps[_currentStep];

            // Icon & title & body
            StepIcon.Glyph  = step.Icon;
            StepTitle.Text  = step.Title;
            StepBody.Text   = step.Body;

            // Settings hint / admin warning banners
            HintBorder.Visibility = step.ShowHint      ? Visibility.Visible : Visibility.Collapsed;
            WarnBorder.Visibility = step.ShowAdminWarn ? Visibility.Visible : Visibility.Collapsed;

            // Back button
            BackButton.Visibility = _currentStep == 0 ? Visibility.Collapsed : Visibility.Visible;

            // Next / Get Started button
            bool isLast = _currentStep == _steps.Length - 1;
            NextButton.Content = isLast ? L("Dialog.GetStarted", "Get Started") : L("Dialog.Next", "Next");

            // Highlight active dot
            for (int i = 0; i < _dots.Length; i++)
            {
                if (i == _currentStep)
                {
                    _dots[i].Fill = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
                    _dots[i].Opacity = 1.0;
                }
                else
                {
                    // Use a brush that works in both themes
                    _dots[i].Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                    _dots[i].Opacity = 0.3;
                }
            }
        }

        // ── Navigation ───────────────────────────────────────────────────────────
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < _steps.Length - 1)
            {
                _currentStep++;
                UpdateStep();
            }
            else
            {
                // Final step — the Closed handler marks onboarding complete.
                this.Hide();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 0)
            {
                _currentStep--;
                UpdateStep();
            }
        }
    }
}
