using System.Runtime.InteropServices;

namespace FluentTaskScheduler.Shell;

/// <summary>
/// The file dialogs of Win32. The pickers of the Windows Runtime refuse to open in an elevated
/// process, so that is where these are used.
/// </summary>
internal static class ClassicFileDialog
{
    // Longer than MAX_PATH: the dialog also returns long paths.
    private const int PathCapacity = 2048;

    /// <param name="filter">Pairs of description and pattern, separated by '|', e.g. "Task XML (*.xml)|*.xml|All files (*.*)|*.*".</param>
    /// <returns>The chosen file, or null if the user cancelled.</returns>
    public static string? PickOpenFile(nint owner, string title, string filter) =>
        Show(owner, title, filter, defaultExtension: null, suggestedFileName: "", OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR, save: false);

    /// <inheritdoc cref="PickOpenFile"/>
    public static string? PickSaveFile(nint owner, string title, string filter, string defaultExtension, string suggestedFileName) =>
        Show(owner, title, filter, defaultExtension, suggestedFileName, OFN_OVERWRITEPROMPT | OFN_HIDEREADONLY | OFN_NOCHANGEDIR, save: true);

    private static string? Show(nint owner, string title, string filter, string? defaultExtension, string suggestedFileName, int flags, bool save)
    {
        // The dialog writes the chosen path into this buffer, so it has to be native memory:
        // a managed string must not be written to.
        nint path = Marshal.AllocHGlobal(PathCapacity * sizeof(char));
        try
        {
            char[] initial = new char[PathCapacity];
            suggestedFileName.CopyTo(0, initial, 0, Math.Min(suggestedFileName.Length, PathCapacity - 1));
            Marshal.Copy(initial, 0, path, PathCapacity);

            var dialog = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = owner,
                lpstrTitle = title,
                lpstrFilter = filter.Replace('|', '\0') + '\0',
                lpstrFile = path,
                nMaxFile = PathCapacity,
                lpstrDefExt = defaultExtension,
                Flags = flags
            };
            bool chosen = save ? GetSaveFileNameW(ref dialog) : GetOpenFileNameW(ref dialog);
            return chosen ? Marshal.PtrToStringUni(path) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(path);
        }
    }

    private const int OFN_OVERWRITEPROMPT = 0x0002, OFN_HIDEREADONLY = 0x0004, OFN_NOCHANGEDIR = 0x0008;
    private const int OFN_PATHMUSTEXIST = 0x0800, OFN_FILEMUSTEXIST = 0x1000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public string? lpstrFilter;
        public string? lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public string? lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public string? lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAMEW dialog);
}
