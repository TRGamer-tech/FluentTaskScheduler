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
            public string lpstrFile;
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

        public static string? PickSaveFile(IntPtr hwnd, string title, string filter, string defExt, string fileName = "")
        {
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;
            ofn.lpstrTitle = title;
            ofn.lpstrFilter = filter.Replace('|', '\0') + '\0';
            
            // Initialize file buffer
            var fileBuffer = fileName.PadRight(2048, '\0');
            ofn.lpstrFile = fileBuffer;
            ofn.nMaxFile = ofn.lpstrFile.Length;
            
            ofn.lpstrDefExt = defExt;
            ofn.nFlags = 0x00000002 | 0x00000008 | 0x00000004; // OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR | OFN_HIDEREADONLY

            if (GetSaveFileName(ref ofn))
            {
                return ofn.lpstrFile.Split('\0')[0];
            }
            return null;
        }

        public static string? PickOpenFile(IntPtr hwnd, string title, string filter)
        {
            var ofn = new OpenFileName();
            ofn.lStructSize = Marshal.SizeOf(ofn);
            ofn.hwndOwner = hwnd;
            ofn.lpstrTitle = title;
            ofn.lpstrFilter = filter.Replace('|', '\0') + '\0';
            
            ofn.lpstrFile = new string('\0', 2048);
            ofn.nMaxFile = ofn.lpstrFile.Length;
            
            ofn.nFlags = 0x00000800 | 0x00000008 | 0x00001000; // OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_PATHMUSTEXIST

            if (GetOpenFileName(ref ofn))
            {
                return ofn.lpstrFile.Split('\0')[0];
            }
            return null;
        }
    }
}
