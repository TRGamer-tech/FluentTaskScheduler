using System;
using System.Runtime.InteropServices;

namespace FluentTaskScheduler.Helpers
{
    public static class Win32FilePicker
    {
        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName(ref OpenFileName ofn);

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetSaveFileName(ref OpenFileName ofn);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct OpenFileName
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string lpstrFilter;
            public string lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            // Marshaled manually as a pinned native buffer (not a managed string) — GetOpenFileName/
            // GetSaveFileName write the chosen path back into this buffer, and writing into a
            // managed System.String from native code is undefined behavior (strings are immutable
            // and may be interned; see 3.7).
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string lpstrFileTitle;
            public int nMaxFileTitle;
            public string lpstrInitialDir;
            public string lpstrTitle;
            public int nFlags;
            public short nFileOffset;
            public short nFileExtension;
            public string lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        private const int FileBufferChars = 2048;

        /// <summary>Allocates an unmanaged, zero-initialized UTF-16 buffer seeded with <paramref name="seed"/>.</summary>
        private static IntPtr AllocFileBuffer(string seed)
        {
            IntPtr buffer = Marshal.AllocHGlobal(FileBufferChars * sizeof(char));
            var chars = new char[FileBufferChars];
            seed ??= "";
            seed.CopyTo(0, chars, 0, Math.Min(seed.Length, FileBufferChars - 1));
            Marshal.Copy(chars, 0, buffer, FileBufferChars);
            return buffer;
        }

        public static string? PickSaveFile(IntPtr hwnd, string title, string filter, string defExt, string fileName = "")
        {
            IntPtr fileBuffer = AllocFileBuffer(fileName);
            try
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(ofn);
                ofn.hwndOwner = hwnd;
                ofn.lpstrTitle = title;
                ofn.lpstrFilter = filter.Replace('|', '\0') + '\0';
                ofn.lpstrFile = fileBuffer;
                ofn.nMaxFile = FileBufferChars;
                ofn.lpstrDefExt = defExt;
                ofn.nFlags = 0x00000002 | 0x00000008 | 0x00000004; // OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR | OFN_HIDEREADONLY

                return GetSaveFileName(ref ofn) ? Marshal.PtrToStringUni(fileBuffer) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
            }
        }

        public static string? PickOpenFile(IntPtr hwnd, string title, string filter)
        {
            IntPtr fileBuffer = AllocFileBuffer("");
            try
            {
                var ofn = new OpenFileName();
                ofn.lStructSize = Marshal.SizeOf(ofn);
                ofn.hwndOwner = hwnd;
                ofn.lpstrTitle = title;
                ofn.lpstrFilter = filter.Replace('|', '\0') + '\0';
                ofn.lpstrFile = fileBuffer;
                ofn.nMaxFile = FileBufferChars;
                ofn.nFlags = 0x00000800 | 0x00000008 | 0x00001000; // OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST

                return GetOpenFileName(ref ofn) ? Marshal.PtrToStringUni(fileBuffer) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(fileBuffer);
            }
        }
    }
}
