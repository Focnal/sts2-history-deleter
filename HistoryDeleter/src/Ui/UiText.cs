using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace Mono.Sts2.HistoryDeleter.Ui;

/// <summary>
/// Minimal bilingual labels. The mod ships no .pck, so instead of a mod localization table we just
/// follow the language the game itself is running in.
/// </summary>
internal static class UiText
{
    internal static bool Chinese
    {
        get
        {
            try
            {
                var language = LocManager.Instance.Language;
                return !string.IsNullOrEmpty(language) &&
                       language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    internal static string Delete => Chinese ? "删除此局记录" : "Delete this run";

    internal static string Deleted => Chinese ? "已删除" : "Deleted";

    internal static string Failed => Chinese ? "删除失败" : "Delete failed";
}
