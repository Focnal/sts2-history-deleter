using MegaCrit.Sts2.Core.Localization;

namespace Mono.Sts2.HistoryDeleter.Ui;

/// <summary>
/// Supplies the confirmation dialog's header/body texts.
///
/// The vanilla popup API (<c>NGenericPopup.WaitForConfirmation</c>) only accepts <see cref="LocString" />,
/// and a <see cref="LocString" /> is just a (table, key) pair resolved through <see cref="LocManager" />.
/// A code-only mod cannot ship a localization table (those must live inside a .pck), so we merge two
/// namespaced keys into the always-loaded <c>main_menu_ui</c> table at dialog time.
/// <see cref="LocTable.MergeWith" /> and <see cref="LocManager.GetTable" /> are public game APIs, so no
/// reflection is involved. Merging on every dialog also keeps working after a language switch, because the
/// game rebuilds its tables on locale change.
/// </summary>
internal static class ModLoc
{
    /// <summary>Table the vanilla quit confirmation already uses; it is always loaded.</summary>
    internal const string Table = "main_menu_ui";

    internal const string CancelKey = "GENERIC_POPUP.cancel";
    internal const string ConfirmKey = "GENERIC_POPUP.confirm";

    private const string HeaderKey = "HISTORY_DELETER.CONFIRM.header";
    private const string BodyKey = "HISTORY_DELETER.CONFIRM.body";

    internal static bool TryCreateConfirmStrings(out LocString header, out LocString body, out string error)
    {
        header = null!;
        body = null!;
        error = string.Empty;

        try
        {
            var manager = LocManager.Instance;
            if (manager == null)
            {
                error = "LocManager.Instance is null";
                return false;
            }

            manager.GetTable(Table).MergeWith(BuildEntries(UiText.Chinese));

            header = new LocString(Table, HeaderKey);
            body = new LocString(Table, BodyKey);

            // Resolve once so a missing key fails here (with a clear log) instead of inside the popup.
            _ = header.GetFormattedText();
            _ = body.GetFormattedText();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    private static Dictionary<string, string> BuildEntries(bool chinese)
    {
        return chinese
            ? new Dictionary<string, string>
            {
                [HeaderKey] = "删除此局记录？",
                [BodyKey] = "将永久删除当前查看的这一局记录，包括它的 Steam 云端副本。\n此操作无法撤销。",
            }
            : new Dictionary<string, string>
            {
                [HeaderKey] = "Delete this run?",
                [BodyKey] = "This permanently deletes the run you are viewing, including its Steam Cloud copy.\nThis cannot be undone.",
            };
    }
}
