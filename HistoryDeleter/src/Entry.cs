using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using Mono.Sts2.HistoryDeleter.Patches;
using STS2RitsuLib;
using STS2RitsuLib.Interop;
using STS2RitsuLib.Patching.Core;

namespace Mono.Sts2.HistoryDeleter;

/// <summary>
/// Mod entry point. The game calls <see cref="Initialize"/> through <see cref="ModInitializerAttribute"/>.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public const string ModId = "HistoryDeleter";

    public static Logger Logger { get; private set; } = null!;

    public static void Initialize()
    {
        Logger = RitsuLibFramework.CreateLogger(ModId);
        Logger.Info("initializing");

        var assembly = Assembly.GetExecutingAssembly();
        // Registers this assembly with RitsuLib so CLR attribute based registration would work too.
        ModTypeDiscoveryHub.RegisterModAssembly(ModId, assembly);

        var patcher = RitsuLibFramework.CreatePatcher(ModId, "run_history");
        patcher.RegisterPatch<RunHistoryReadyPatch>();
        patcher.RegisterPatch<RunHistoryOpenedPatch>();
        RitsuLibFramework.ApplyRequiredPatcher(patcher, OnCriticalPatchFailure);

        Logger.Info($"ready (run history access available: {RunHistoryAccess.IsAvailable})");
    }

    private static void OnCriticalPatchFailure()
    {
        Logger.Error("required patch could not be applied; the delete button stays unavailable.");
    }
}
