using System.Collections.ObjectModel;

namespace FluentTaskScheduler.ViewModels;

internal static class CollectionSync
{
    /// <summary>
    /// Makes <paramref name="shown"/> equal to <paramref name="wanted"/> by removing, moving and
    /// inserting only the entries that differ. An entry that stays keeps its place in the
    /// collection, so the list control showing it keeps its scroll position, focus and selection.
    /// </summary>
    /// <remarks>
    /// Entries are compared with Equals: records by value, other classes by reference. An entry
    /// that equals one that is shown already stays the instance that is shown. Of several equal
    /// entries, the first shown stands for the first wanted, and so on.
    /// </remarks>
    public static void SyncWith<T>(this ObservableCollection<T> shown, IReadOnlyList<T> wanted)
    {
        var keep = new HashSet<T>(wanted);
        for (int i = shown.Count - 1; i >= 0; i--)
            if (!keep.Contains(shown[i])) shown.RemoveAt(i);

        EqualityComparer<T> same = EqualityComparer<T>.Default;
        for (int i = 0; i < wanted.Count; i++)
        {
            if (i < shown.Count && same.Equals(shown[i], wanted[i])) continue;

            // Searched from here on: an entry equal to one placed already is another one.
            int current = -1;
            for (int j = i + 1; j < shown.Count && current < 0; j++)
                if (same.Equals(shown[j], wanted[i])) current = j;

            if (current < 0) shown.Insert(i, wanted[i]);
            else shown.Move(current, i);
        }

        // What is left over are entries wanted fewer times than they were shown.
        while (shown.Count > wanted.Count) shown.RemoveAt(shown.Count - 1);
    }
}
