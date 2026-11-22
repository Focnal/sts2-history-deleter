using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using Mono.Sts2.HistoryDeleter.Ui;
using STS2RitsuLib.Patching.Models;

namespace Mono.Sts2.HistoryDeleter.Patches;

/// <summary>Adds the delete button once the vanilla screen has bound all of its nodes.</summary>
internal sealed class RunHistoryReadyPatch : IPatchMethod
{
    public static string PatchId => "history_deleter/run_history_ready";

    public static string Description => "Run history: add the delete button to NRunHistory._Ready";

    public static bool IsCritical => true;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NRunHistory), "_Ready"),
    ];

    public static void Postfix(NRunHistory __instance)
    {
        DeleteRunButton.Install(__instance);
    }
}

/// <summary>Resets the button state whenever the screen is opened again (instances can be reused).</summary>
internal sealed class RunHistoryOpenedPatch : IPatchMethod
{
    public static string PatchId => "history_deleter/run_history_opened";

    public static string Description => "Run history: reset the delete button on OnSubmenuOpened";

    public static bool IsCritical => false;

    public static ModPatchTarget[] GetTargets() =>
    [
        new(typeof(NRunHistory), "OnSubmenuOpened"),
    ];

    public static void Postfix(NRunHistory __instance)
    {
        DeleteRunButton.ResetVisual(__instance);
    }
}
