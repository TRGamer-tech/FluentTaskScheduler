using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FluentTaskScheduler.ViewModels;
using FluentTaskScheduler.Services;

namespace FluentTaskScheduler
{
    public sealed partial class ScriptEditorPage : Page
    {
        private Process? _runningProcess;
        private readonly DispatcherTimer _highlightTimer;
        private bool _isHighlighting = false;

        private static string L(string key, string fallback) => LocalizationService.GetString(key, fallback);

        public ScriptEditorPage()
        {
            this.InitializeComponent();
            _highlightTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _highlightTimer.Tick += (s, e) => { _highlightTimer.Stop(); HighlightCode(); };

            LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
            this.Unloaded += (s, e) => LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
            this.ActualThemeChanged += (s, e) => HighlightCode();
            ApplyLocalizedUi();
        }

        private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
        {
            if (DispatcherQueue == null) return;
            DispatcherQueue.TryEnqueue(ApplyLocalizedUi);
        }

        /// <summary>
        /// Fills every label from LocalizationService. These used to rely on x:Uid, which resolves
        /// against the Windows display language rather than the app's language setting.
        /// </summary>
        private void ApplyLocalizedUi()
        {
            RunButton.Label = L("ScriptEditor_RunBtn.Label", "Run");
            StopButton.Label = L("ScriptEditor_StopBtn.Label", "Stop");
            SaveButton.Label = L("ScriptEditor_SaveBtn.Label", "Save to Library");
            ClearButton.Label = L("ScriptEditor_ClearBtn.Label", "Clear Console");

            CodeEditor.Header = L("ScriptEditor_CodeEditor.Header", "PowerShell Script");
            CodeEditor.PlaceholderText = L("ScriptEditor_CodeEditor.PlaceholderText", "Write your PowerShell script here...");
            OutputHeaderText.Text = L("ScriptEditor_OutputHeader.Text", "Output Console");
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            CodeEditor.Focus(FocusState.Programmatic);
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            string code;
            CodeEditor.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out code);
            if (string.IsNullOrWhiteSpace(code)) return;

            await RunScript(code);
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_runningProcess != null && !_runningProcess.HasExited)
                {
                    _runningProcess.Kill(true);
                    AppendOutput("\n" + LocalizationService.GetString("ScriptEditor.Status.Stopped", "[Stopped by user]") + "\n", Colors.Red);
                }
            }
            catch { }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            OutputParagraph.Inlines.Clear();
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            string code;
            CodeEditor.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out code);
            if (string.IsNullOrWhiteSpace(code)) return;

            var dialog = new ContentDialog
            {
                Title = LocalizationService.GetString("ScriptEditor.SaveDialog.Title", "Save Script to Library"),
                PrimaryButtonText = LocalizationService.GetString("Dialog.Common.Save", "Save"),
                CloseButtonText = LocalizationService.GetString("Dialog.Common.Cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var stack = new StackPanel { Spacing = 12 };
            var nameBox = new TextBox { Header = LocalizationService.GetString("ScriptEditor.SaveDialog.NameHeader", "Template Name"), PlaceholderText = "e.g. My Custom Cleanup" };
            var descBox = new TextBox { Header = LocalizationService.GetString("ScriptEditor.SaveDialog.DescHeader", "Description"), PlaceholderText = "What does this script do?" };
            stack.Children.Add(nameBox);
            stack.Children.Add(descBox);
            dialog.Content = stack;

            if (await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
            {
                // Save script to a temporary file in local app data
                string scriptsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentTaskScheduler", "Scripts");
                Directory.CreateDirectory(scriptsDir);
                string fileName = $"{Guid.NewGuid()}.ps1";
                string fullPath = Path.Combine(scriptsDir, fileName);
                File.WriteAllText(fullPath, code);

                var viewModel = new ScriptLibraryViewModel();
                await viewModel.LoadScriptsAsync();
                viewModel.AddUserTemplate(new ScriptTemplateModel
                {
                    Name = nameBox.Text,
                    Description = descBox.Text,
                    Command = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{fullPath}\"",
                    IsUserTemplate = true
                });

                AppendOutput("\n" + string.Format(LocalizationService.GetString("ScriptEditor.Status.Saved", "[Saved to Library as '{0}']"), nameBox.Text) + "\n", Colors.Green);
            }
        }

        private async Task RunScript(string code)
        {
            RunButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            OutputParagraph.Inlines.Clear();
            AppendOutput(LocalizationService.GetString("ScriptEditor.Status.Starting", "[Starting PowerShell...]") + "\n", Colors.Gray);

            string tempFile = Path.Combine(Path.GetTempPath(), $"ft_temp_{Guid.NewGuid()}.ps1");
            File.WriteAllText(tempFile, code);

            try
            {
                _runningProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -File \"{tempFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8
                    },
                    EnableRaisingEvents = true
                };

                _runningProcess.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data + "\n", Colors.LightGray); };
                _runningProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data + "\n", Colors.Red); };

                _runningProcess.Start();
                _runningProcess.BeginOutputReadLine();
                _runningProcess.BeginErrorReadLine();

                await _runningProcess.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                AppendOutput($"Error: {ex.Message}\n", Colors.Red);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                RunButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                AppendOutput("\n" + LocalizationService.GetString("ScriptEditor.Status.Exited", "[Process Exited]") + "\n", Colors.Gray);
            }
        }

        private void AppendOutput(string text, Windows.UI.Color color)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                OutputParagraph.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = text,
                    Foreground = new SolidColorBrush(color)
                });
                OutputScrollViewer.ChangeView(null, OutputScrollViewer.ScrollableHeight, null, disableAnimation: true);
            });
        }

        private void CodeEditor_TextChanged(object sender, RoutedEventArgs e)
        {
            if (_isHighlighting) return;
            _highlightTimer.Stop();
            _highlightTimer.Start();
        }

        private void HighlightCode()
        {
            _isHighlighting = true;
            
            // Basic PowerShell highlighting
            string[] keywords = { "if", "else", "elseif", "foreach", "while", "function", "return", "try", "catch", "finally", "throw", "break", "continue", "param", "in" };
            
            // We need to save the selection to restore it
            var selection = CodeEditor.Document.Selection;
            int start = selection.StartPosition;
            int end = selection.EndPosition;

            // Clear formatting first — theme-aware, not hardcoded white (which is invisible on the
            // light theme's editor background; see 2.9).
            var defaultColor = ActualTheme == ElementTheme.Dark ? Colors.White : Colors.Black;
            var allRange = CodeEditor.Document.GetRange(0, Microsoft.UI.Text.TextConstants.MaxUnitCount);
            allRange.CharacterFormat.ForegroundColor = defaultColor;

            string text;
            allRange.GetText(Microsoft.UI.Text.TextGetOptions.None, out text);

            foreach (var word in keywords)
            {
                HighlightWord(word, Colors.CornflowerBlue);
            }

            // Restore selection
            CodeEditor.Document.Selection.SetRange(start, end);
            _isHighlighting = false;
        }

        private void HighlightWord(string word, Windows.UI.Color color)
        {
            int start = 0;
            while (true)
            {
                var range = CodeEditor.Document.GetRange(start, Microsoft.UI.Text.TextConstants.MaxUnitCount);
                // FindOptions.Word restricts matches to whole words, so e.g. the keyword "in" no
                // longer lights up the "in" inside "string" (see 2.9).
                int found = range.FindText(word, Microsoft.UI.Text.TextConstants.MaxUnitCount, Microsoft.UI.Text.FindOptions.Word);
                if (found <= 0) break;

                range.CharacterFormat.ForegroundColor = color;
                start = range.EndPosition;
            }
        }
    }
}
