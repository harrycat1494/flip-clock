using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
#if STORE
using Windows.ApplicationModel;
#endif

namespace FlipClock;

/// <summary>
/// "Start with Windows". The downloaded program writes one entry into the user's own start-up list in the
/// registry. The Microsoft Store version cannot do that (Windows keeps a Store app's registry writes private
/// to the app), so it uses the start-up task that Windows gives a Store app.
/// Nothing here may ever stop the clock: every failure is ignored.
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "FlipClock";
#if STORE
    private const string TaskId = "FlipClockStartup"; // the same name as in the package manifest
#endif

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? fullName);

    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE: an ordinary program, not a Store app

    /// <summary>True when the clock runs as the Microsoft Store version.</summary>
    public static readonly bool FromStore = HasPackage();

    private static bool HasPackage()
    {
        try
        {
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != NoPackage;
        }
        catch { return false; }
    }

    /// <summary>Whether the clock starts with Windows, as last looked up (see <see cref="Refresh"/>).</summary>
    public static bool On { get; private set; } = RegistryOn();

    /// <summary>Look it up again, then call <paramref name="done"/> (on the same thread).</summary>
    public static async void Refresh(Action? done = null)
    {
        try
        {
#if STORE
            if (FromStore)
            {
                var task = await StartupTask.GetAsync(TaskId);
                On = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            else
#endif
            {
                On = RegistryOn();
                await Task.CompletedTask;
            }
        }
        catch { /* leave it as it was */ }
        done?.Invoke();
    }

    /// <summary>
    /// Switch it on or off, then call <paramref name="done"/>. If Windows refuses (the user or the PC's
    /// administrator switched the clock off in Windows' own start-up list), <paramref name="refused"/> gets
    /// a sentence that says where to switch it on.
    /// </summary>
    public static async void Set(bool on, Action? done = null, Action<string>? refused = null)
    {
        try
        {
#if STORE
            if (FromStore)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (on)
                {
                    var state = await task.RequestEnableAsync();
                    if (state == StartupTaskState.DisabledByUser)
                        refused?.Invoke("Windows has Flip Clock switched off in its own start-up list.\n\n" +
                                        "Switch it on there: Windows Settings, Apps, Startup.");
                    else if (state == StartupTaskState.DisabledByPolicy)
                        refused?.Invoke("Starting with Windows is switched off for this PC by its administrator.");
                    On = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
                }
                else
                {
                    task.Disable();
                    On = false;
                }
            }
            else
#endif
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key != null)
                {
                    if (on && Environment.ProcessPath is string exe) key.SetValue(RunName, $"\"{exe}\"");
                    else key.DeleteValue(RunName, throwOnMissingValue: false);
                }
                On = RegistryOn();
                await Task.CompletedTask;
            }
        }
        catch { /* no access: leave it as it is */ }
        done?.Invoke();
    }

    /// <summary>
    /// If "Start with Windows" is ticked but still names another file (the old copy, after an update into
    /// a different folder), point it at the program that is running now. Otherwise the old version would
    /// come back at the next sign-in. Does nothing if the option is not ticked, and nothing in the Store
    /// version, where Windows itself knows which program to start.
    /// </summary>
    public static void KeepCurrent()
    {
        if (FromStore) return;
        try
        {
            if (Environment.ProcessPath is not string exe) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(RunName) is not string entry) return;
            string wanted = $"\"{exe}\"";
            if (!string.Equals(entry, wanted, StringComparison.OrdinalIgnoreCase)) key.SetValue(RunName, wanted);
        }
        catch { /* no access: leave it as it is */ }
    }

    private static bool RegistryOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        catch { return false; }
    }
}
