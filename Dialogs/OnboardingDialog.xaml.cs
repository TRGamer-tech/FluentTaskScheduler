using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace FluentTaskScheduler.Dialogs
{
    public sealed partial class OnboardingDialog : ContentDialog
    {
        // ── Step Definitions ─────────────────────────────────────────────────────
        private readonly struct Step
        {
            public string Icon      { get; init; }
            public string Title     { get; init; }
            public string Body      { get; init; }
            public bool ShowHint    { get; init; }   // "find this again in Settings" hint
            public bool ShowAdminWarn { get; init; } // admin-rights warning
        }

        private static readonly Step[] Steps = new[]
        {
            new Step
            {
                Icon          = "\uE8A1",   // Calendar / Scheduler
                Title         = "Welcome to FluentTaskScheduler",
                Body          = "Manage Windows Task Scheduler with a modern, fluent interface — no XML, no fuss.",
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE710",   // Add / Plus
                Title         = "Create your first task",
                Body          = "Hit + New Task to schedule any program, script, or command to run automatically — daily, on login, on an event, and more.",
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE8B7",   // Folder
                Title         = "Organise with folders",
                Body          = "Group related tasks into folders using the sidebar, just like Windows Explorer. Your last folder is remembered across restarts.",
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE9F9",   // Chart / History
                Title         = "Track history & status",
                Body          = "Click any task to see its run history, success and failure counts, and live running status — all in one place.",
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE895",   // Sync / Updates
                Title         = "Always up to date",
                Body          = "FluentTaskScheduler checks for updates automatically every time it starts. You can also trigger a manual check at any time from Settings → About → Check for Updates.",
                ShowHint      = false,
                ShowAdminWarn = false
            },
            new Step
            {
                Icon          = "\uE773",   // Search
                Title         = "Discover existing tasks",
                Body          = "Use Task Discovery to scan the Windows Event Log and automatically import tasks created by other applications — no manual recreation needed.",
                ShowHint      = false,
                ShowAdminWarn = true        // admin required for Event Log access
            },
            new Step
            {
                Icon          = "\uE713",   // Settings
                Title         = "Tune it to your liking",
                Body          = "Head to Settings to enable Mica backdrop, minimise to tray, configure logging, run on startup, and more.",
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
            BuildDots();
            UpdateStep();
        }

        // ── Dot indicators ───────────────────────────────────────────────────────
        private void BuildDots()
        {
            _dots = new Ellipse[Steps.Length];
            for (int i = 0; i < Steps.Length; i++)
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
            var step = Steps[_currentStep];

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
            bool isLast = _currentStep == Steps.Length - 1;
            NextButton.Content = isLast ? "Get Started" : "Next";

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
            if (_currentStep < Steps.Length - 1)
            {
                _currentStep++;
                UpdateStep();
            }
            else
            {
                // Final step — mark onboarding complete and close
                Services.SettingsService.HasCompletedOnboarding = true;
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
