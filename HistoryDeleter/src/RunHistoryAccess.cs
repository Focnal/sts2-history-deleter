using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;

namespace Mono.Sts2.HistoryDeleter;

/// <summary>
/// Read/write access to the private state of the vanilla run history screen.
/// The screen keeps its own list of history file names plus a cursor index; we have to keep both in sync
/// with the files on disk, otherwise the arrows and the detail view point at stale entries.
/// </summary>
internal static class RunHistoryAccess
{
    private static readonly FieldInfo? RunNamesField = AccessTools.Field(typeof(NRunHistory), "_runNames");
    private static readonly FieldInfo? IndexField = AccessTools.Field(typeof(NRunHistory), "_index");
    private static readonly FieldInfo? StackField = AccessTools.Field(typeof(NSubmenu), "_stack");

    private static readonly MethodInfo? RefreshMethod = AccessTools.Method(
        typeof(NRunHistory),
        "RefreshAndSelectRun",
        [typeof(int)]);

    internal static bool IsAvailable =>
        RunNamesField != null && IndexField != null && RefreshMethod != null;

    internal static List<string>? GetRunNames(NRunHistory screen)
    {
        return RunNamesField?.GetValue(screen) as List<string>;
    }

    /// <summary>Current cursor, or -1 when the screen shows a preview that is not part of the saved list.</summary>
    internal static int GetIndex(NRunHistory screen)
    {
        return IndexField?.GetValue(screen) is int index ? index : -1;
    }

    internal static void SetIndex(NRunHistory screen, int index)
    {
        IndexField?.SetValue(screen, index);
    }

    /// <summary>Reloads the detail view for <paramref name="index"/>; also fixes arrow visibility.</summary>
    internal static void RefreshAndSelect(NRunHistory screen, int index)
    {
        try
        {
            RefreshMethod?.Invoke(screen, [index]);
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"RefreshAndSelectRun({index}) failed: {ex.GetBaseException().Message}");
        }
    }

    internal static NSubmenuStack? GetStack(NRunHistory screen)
    {
        return StackField?.GetValue(screen) as NSubmenuStack;
    }
}
