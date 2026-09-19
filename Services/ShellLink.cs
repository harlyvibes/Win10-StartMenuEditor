using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace StartMenuEditor.Services;

public sealed class ShortcutInfo
{
    public string TargetPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Reads and writes .lnk files through the shell's IShellLink COM object.</summary>
public static class ShellLink
{
    private const int StgmRead = 0;
    private const int StgmReadWrite = 2;
    private const int MaxPath = 1024;

    public static ShortcutInfo Read(string path)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(path, StgmRead);
            return Get(link);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// Saves only the fields that differ from what the shortcut already holds. Re-setting an
    /// unchanged target would discard the link's shell item ID list, which breaks shortcuts
    /// that point at special shell items rather than a file path.
    /// </summary>
    public static void Write(string path, ShortcutInfo info)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            var file = (IPersistFile)link;
            file.Load(path, StgmReadWrite);
            var current = Get(link);

            if (info.TargetPath != current.TargetPath) link.SetPath(info.TargetPath);
            if (info.Arguments != current.Arguments) link.SetArguments(info.Arguments);
            if (info.WorkingDirectory != current.WorkingDirectory) link.SetWorkingDirectory(info.WorkingDirectory);
            if (info.Description != current.Description) link.SetDescription(info.Description);

            file.Save(path, true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    private static ShortcutInfo Get(IShellLinkW link)
    {
        var target = new StringBuilder(MaxPath);
        var args = new StringBuilder(MaxPath);
        var dir = new StringBuilder(MaxPath);
        var desc = new StringBuilder(MaxPath);

        link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
        link.GetArguments(args, args.Capacity);
        link.GetWorkingDirectory(dir, dir.Capacity);
        link.GetDescription(desc, desc.Capacity);

        return new ShortcutInfo
        {
            TargetPath = target.ToString(),
            Arguments = args.ToString(),
            WorkingDirectory = dir.ToString(),
            Description = desc.ToString(),
        };
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
