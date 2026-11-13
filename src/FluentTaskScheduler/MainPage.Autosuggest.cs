using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using FluentTaskScheduler.Models;
using FluentTaskScheduler.Models.Enums;
using FluentTaskScheduler.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Dispatching;
using FluentTaskScheduler.ViewModels;
using System;
using Windows.ApplicationModel.DataTransfer;

namespace FluentTaskScheduler
{
    /// <summary>Category and tag autosuggest boxes.</summary>
    public sealed partial class MainPage
    {
        // --- AutoSuggest Interactivity ---

        private void EditTaskCategory_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox asb) asb.IsSuggestionListOpen = true;
        }

        private void EditTaskCategory_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                var query = sender.Text.Trim();
                var suggestions = new List<string>();

                if (string.IsNullOrEmpty(query))
                {
                    suggestions.AddRange(ViewModel.SavedCategories);
                }
                else
                {
                    suggestions.AddRange(ViewModel.SavedCategories
                        .Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase))
                        .ToList());

                    if (!ViewModel.SavedCategories.Any(c => c.Equals(query, StringComparison.OrdinalIgnoreCase)))
                    {
                        suggestions.Add($"Add \"{query}\"");
                    }
                }
                sender.ItemsSource = suggestions;
            }
        }

        private void EditTaskCategory_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var selected = args.SelectedItem.ToString() ?? "";
            if (selected.StartsWith("Add \"") && selected.EndsWith("\""))
            {
                var newCat = selected.Substring(5, selected.Length - 6);
                if (!ViewModel.SavedCategories.Any(c => c.Equals(newCat, StringComparison.OrdinalIgnoreCase)))
                {
                    Settings.AddSavedCategory(newCat);
                    ViewModel.RefreshSavedCategories();
                }
                sender.Text = newCat;
            }
            else
            {
                sender.Text = selected;
            }
        }

        private void EditTaskTags_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is AutoSuggestBox asb)
            {
                RefreshTagSuggestions(asb);
                asb.IsSuggestionListOpen = true;
            }
        }

        private void EditTaskTags_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                RefreshTagSuggestions(sender);
            }
        }

        private void RefreshTagSuggestions(AutoSuggestBox sender)
        {
            var currentText = sender.Text ?? "";
            var parts = currentText.Split(',').Select(p => p.Trim()).ToList();
            var lastPart = parts.LastOrDefault() ?? "";
            var existingTags = (parts.Count > 1) ? parts.Take(parts.Count - 1).ToList() : new List<string>();

            var availableTags = ViewModel.SavedTags
                .Where(t => !existingTags.Any(et => et.Equals(t, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var suggestions = new List<string>();
            bool isExactMatch = ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(lastPart))
            {
                suggestions.AddRange(availableTags);
            }
            else if (isExactMatch)
            {
                // If the last part is a complete tag, show all other available tags
                suggestions.AddRange(availableTags.Where(t => !t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                var filtered = availableTags
                    .Where(t => t.Contains(lastPart, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                suggestions.AddRange(filtered);

                if (!ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)))
                {
                    suggestions.Add($"Add \"{lastPart}\"");
                }
            }
            sender.ItemsSource = suggestions;
        }

        private void EditTaskTags_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is not string selected) return;
            
            var currentText = sender.Text ?? "";
            var parts = currentText.Split(',').Select(p => p.Trim()).ToList();
            var lastPart = parts.LastOrDefault() ?? "";
            if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);

            string finalTag = selected;
            if (selected.StartsWith("Add \"") && selected.EndsWith("\""))
            {
                finalTag = selected.Substring(5, selected.Length - 6);
                if (!ViewModel.SavedTags.Any(t => t.Equals(finalTag, StringComparison.OrdinalIgnoreCase)))
                {
                    Settings.AddSavedTag(finalTag);
                    ViewModel.RefreshSavedCategories();
                }
            }

            // If the last part was already a complete tag and we chose something else, restore it.
            if (ViewModel.SavedTags.Any(t => t.Equals(lastPart, StringComparison.OrdinalIgnoreCase)) && 
                !lastPart.Equals(finalTag, StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(lastPart);
            }

            if (!parts.Any(p => p.Equals(finalTag, StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(finalTag);
            }

            sender.Text = string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p))) + ", ";
        }
        private void Settings_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(this.SettingsAnimatedIcon, "PointerOver");
        }

        private void Settings_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            AnimatedIcon.SetState(this.SettingsAnimatedIcon, "Normal");
        }

        private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Models.TaskHistoryEntry entry)
            {
                var detailView = new Dialogs.HistoryEntryDetailDialog(entry);
                var flyout = new Flyout
                {
                    Content = detailView,
                    Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
                    FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
                    {
                        Setters = { new Setter(FlyoutPresenter.MaxWidthProperty, 1000) }
                    }
                };
                
                if (sender is ListView lv)
                {
                    var container = lv.ContainerFromItem(e.ClickedItem) as FrameworkElement;
                    flyout.ShowAt(container ?? lv);
                }
            }
        }
    }
}
