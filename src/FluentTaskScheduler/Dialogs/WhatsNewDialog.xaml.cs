using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace FluentTaskScheduler.Dialogs
{
    public sealed partial class WhatsNewDialog : ContentDialog
    {
        public WhatsNewDialog(Services.GitHubRelease release)
        {
            this.InitializeComponent();

            // GitHub can legitimately return null for these fields (e.g. a no-notes release),
            // so null-coalesce everything rather than let the dialog crash on construction.
            string tagName = release.TagName ?? "";
            string name = release.Name ?? "";

            // ── Version badge ────────────────────────────────────────────────────
            ReleaseTagText.Text = tagName;

            // ── Title (strip leading tag prefix if present) ──────────────────────
            string title = name;
            if (!string.IsNullOrEmpty(tagName) && title.StartsWith(tagName, StringComparison.OrdinalIgnoreCase))
                title = title[tagName.Length..].TrimStart(' ', '-', '–');
            ReleaseTitleText.Text = title;

            // ── Markdown body ────────────────────────────────────────────────────
            RenderMarkdown(release.Body ?? "");

            if (Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releaseUri))
            {
                ViewOnGitHubButton.NavigateUri = releaseUri;
            }
            else
            {
                ViewOnGitHubButton.IsEnabled = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Minimal Markdown → RichTextBlock renderer
        // Handles: ## headings, - bullets, **bold**, plain paragraphs
        // ─────────────────────────────────────────────────────────────────────────
        private void RenderMarkdown(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return;

            // Normalise line endings
            string[] lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            Paragraph? currentPara = null;

            void FlushPara()
            {
                if (currentPara != null)
                {
                    ReleaseBodyRtb.Blocks.Add(currentPara);
                    currentPara = null;
                }
            }

            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd();

                // ── Heading (## or #) ────────────────────────────────────────────
                if (line.StartsWith("## ") || line.StartsWith("# "))
                {
                    FlushPara();
                    string headingText = line.TrimStart('#').TrimStart();

                    var p = new Paragraph { Margin = new Thickness(0, 10, 0, 2) };
                    var run = new Run
                    {
                        Text = headingText,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 14
                    };
                    p.Inlines.Add(run);
                    ReleaseBodyRtb.Blocks.Add(p);
                    continue;
                }

                // ── Bullet (- or *) ──────────────────────────────────────────────
                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    FlushPara();
                    string bulletText = line[2..];

                    var p = new Paragraph { Margin = new Thickness(12, 1, 0, 1) };
                    p.Inlines.Add(new Run { Text = "• " });
                    foreach (var inline in ParseInlines(bulletText))
                        p.Inlines.Add(inline);
                    ReleaseBodyRtb.Blocks.Add(p);
                    continue;
                }

                // ── Blank line → flush paragraph ─────────────────────────────────
                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushPara();
                    continue;
                }

                // ── Regular text — accumulate into paragraph ──────────────────────
                if (currentPara == null)
                    currentPara = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                else
                    currentPara.Inlines.Add(new Run { Text = " " }); // word-wrap join

                foreach (var inline in ParseInlines(line))
                    currentPara.Inlines.Add(inline);
            }

            FlushPara();
        }

        /// <summary>
        /// Splits a line into plain <see cref="Run"/> and bold <see cref="Bold"/> inlines
        /// by scanning for **…** markers.
        /// </summary>
        private static IEnumerable<Inline> ParseInlines(string text)
        {
            var result = new List<Inline>();
            int i = 0;
            while (i < text.Length)
            {
                int boldStart = text.IndexOf("**", i, StringComparison.Ordinal);
                if (boldStart == -1)
                {
                    // No more bold markers — rest is plain
                    if (i < text.Length)
                        result.Add(new Run { Text = text[i..] });
                    break;
                }

                // Plain text before the bold marker
                if (boldStart > i)
                    result.Add(new Run { Text = text[i..boldStart] });

                int boldEnd = text.IndexOf("**", boldStart + 2, StringComparison.Ordinal);
                if (boldEnd == -1)
                {
                    // Unclosed marker — treat rest as plain
                    result.Add(new Run { Text = text[boldStart..] });
                    break;
                }

                // Bold span
                string boldContent = text[(boldStart + 2)..boldEnd];
                var bold = new Bold();
                bold.Inlines.Add(new Run { Text = boldContent });
                result.Add(bold);

                i = boldEnd + 2;
            }
            return result;
        }
    }
}
