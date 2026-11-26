using System.Runtime.InteropServices;

namespace FlipClock;

/// <summary>
/// Tells Windows to take the clock's button off the taskbar.
/// The clock should never get one: its window is a tool window with a hidden owner, the two ways Windows
/// documents for staying off the taskbar. A user still reported an icon there, which could not be reproduced,
/// so this says it a third time with the Shell's own instruction (ITaskbarList::DeleteTab).
/// Nothing here may ever stop the clock: every failure is ignored.
/// </summary>
internal static class TaskbarButton
{
    // Identifiers and method order as in Windows' shobjidl_core.h (the same declaration WPF itself uses).
    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr hwnd);
        [PreserveSig] int DeleteTab(IntPtr hwnd);
        [PreserveSig] int ActivateTab(IntPtr hwnd);
        [PreserveSig] int SetActiveAlt(IntPtr hwnd);
    }

    private static readonly Guid TaskbarListClass = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    public static void Remove(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromCLSID(TaskbarListClass);
            if (type == null) return;
            shell = Activator.CreateInstance(type);
            // made new every time on purpose: after Windows Explorer restarts an old one would point nowhere
            if (shell is ITaskbarList list && list.HrInit() >= 0) list.DeleteTab(hwnd);
        }
        catch { /* no taskbar, or Windows says no: the clock carries on */ }
        finally
        {
            try { if (shell != null) Marshal.ReleaseComObject(shell); } catch { }
        }
    }
}
