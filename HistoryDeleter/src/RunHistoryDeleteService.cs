using System.IO;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace Mono.Sts2.HistoryDeleter;

/// <summary>
/// Deletes one run history entry through the game's own <see cref="ISaveStore"/>.
/// Going through the store instead of <see cref="File.Delete(string)"/> matters: with Steam enabled the
/// game writes through <see cref="CloudSaveStore"/>, so deleting only the local file would be
/// re-downloaded from Steam Remote Storage on the next launch.
/// </summary>
internal static class RunHistoryDeleteService
{
    private static readonly System.Reflection.FieldInfo? SaveStoreField =
        AccessTools.Field(typeof(SaveManager), "_saveStore");

    /// <summary>
    /// Resolves the history directory the same way <c>RunHistorySaveManager.SaveHistory</c> does, so it
    /// automatically follows the current scope (profileN vs modded/profileN) and profile.
    /// </summary>
    internal static string? TryGetHistoryDirectory(out string error)
    {
        error = string.Empty;
        try
        {
            var saveManager = SaveManager.Instance;
            if (saveManager == null)
            {
                error = "SaveManager.Instance is null";
                return null;
            }

            return RunHistorySaveManager.GetHistoryPath(saveManager.CurrentProfileId);
        }
        catch (Exception ex)
        {
            error = $"could not resolve the history directory: {ex.GetBaseException().Message}";
            return null;
        }
    }

    /// <summary>
    /// Removes <paramref name="fileName"/> (a bare file name such as "1790235288.run") together with the
    /// local-only ".backup" sibling the game keeps next to most save files.
    /// Returns true when the primary file no longer exists afterwards.
    /// </summary>
    internal static bool TryDelete(string fileName, out string detail)
    {
        detail = string.Empty;

        if (!IsSafeRunFileName(fileName))
        {
            detail = $"refused to delete unsafe history file name '{fileName}'";
            return false;
        }

        var historyDirectory = TryGetHistoryDirectory(out var pathError);
        if (historyDirectory == null)
        {
            detail = pathError;
            return false;
        }

        ISaveStore? store;
        try
        {
            store = SaveStoreField?.GetValue(SaveManager.Instance) as ISaveStore;
        }
        catch (Exception ex)
        {
            detail = $"could not read SaveManager._saveStore: {ex.GetBaseException().Message}";
            return false;
        }

        if (store == null)
        {
            detail = "SaveManager._saveStore is unavailable; nothing was deleted";
            return false;
        }

        var path = Path.Combine(historyDirectory, fileName);
        var backupPath = path + ".backup";

        var removed = 0;
        try
        {
            if (store.FileExists(path))
            {
                store.DeleteFile(path);
                removed++;
            }

            if (store.FileExists(backupPath))
            {
                store.DeleteFile(backupPath);
                removed++;
            }
        }
        catch (Exception ex)
        {
            detail = $"delete threw {ex.GetType().Name}: {ex.GetBaseException().Message}";
            return false;
        }

        if (store.FileExists(path))
        {
            detail = $"'{path}' still exists after delete (Steam Cloud may have refused the removal)";
            return false;
        }

        detail = $"removed {removed} file(s) for '{path}'";
        return true;
    }

    /// <summary>
    /// Defence in depth: only ever touch a plain *.run file directly inside the history directory,
    /// never a name that could escape it.
    /// </summary>
    private static bool IsSafeRunFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (!fileName.EndsWith(".run", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (fileName.IndexOfAny(['/', '\\', ':']) >= 0 || fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }
}
