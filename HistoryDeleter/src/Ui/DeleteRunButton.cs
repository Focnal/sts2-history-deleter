using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.ProfileScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.addons.mega_text;

namespace Mono.Sts2.HistoryDeleter.Ui;

/// <summary>
/// The delete button on the run history screen, reusing the game's own "delete profile" button.
///
/// Visuals are the shipped <c>res://scenes/screens/profiles/profile_delete_button.tscn</c>
/// (trash icon with the HSV shader + a <see cref="MegaLabel" /> that fades in and slides down on hover),
/// so the look and the fly-out text animation are pixel-identical to the profile screen.
///
/// The instantiated node is <see cref="NDeleteProfileButton" />, whose <c>OnRelease</c> would delete a
/// profile, so it is made non-interactive (<c>MouseFilter=Ignore</c>, <c>FocusMode=None</c>) and an
/// invisible <see cref="Button" /> on top handles hover/click instead. Hover animation is replayed by
/// calling the node's own <c>OnFocus</c>/<c>OnUnfocus</c> through reflection, which keeps every tween
/// parameter identical to vanilla.
///
/// Confirmation uses the same popup the main menu shows when quitting:
/// <see cref="NGenericPopup" /> + <see cref="NModalContainer" /> + <see cref="NGenericPopup.WaitForConfirmation" />.
/// </summary>
internal static class DeleteRunButton
{
    private const string VisualName = "HistoryDeleterDeleteButton";
    private const string HitboxName = "HistoryDeleterDeleteButtonHitbox";
    private const string ScenePath = "res://scenes/screens/profiles/profile_delete_button.tscn";

    // Right-aligned with ShareButton and sitting above it.
    //   ShareButton: anchors(1,1), offsets (-220,-112)-(-48,-48), i.e. right edge -48, top -112.
    //   This button: 80x80, right edge -48, top -252 (bottom -172).
    //   The 80px gap equals what the fly-out label needs (label bottom = top + 139.5 at hover scale 1.1),
    //   so the revealed text stops just above the share button instead of covering it.
    private const float OffsetLeft = -128f;
    private const float OffsetTop = -252f;
    private const float OffsetRight = -48f;
    private const float OffsetBottom = -172f;

    /// <summary>Label geometry relative to the 80x80 root: 420px wide, right-aligned, below the icon.</summary>
    private const float LabelLeft = -340f;
    private const float LabelTop = 38f;
    private const float LabelBottom = 90.5f;

    private static readonly MethodInfo? OnFocusMethod =
        AccessTools.Method(typeof(NDeleteProfileButton), "OnFocus");

    private static readonly MethodInfo? OnUnfocusMethod =
        AccessTools.Method(typeof(NDeleteProfileButton), "OnUnfocus");

    private static bool _missingHoverHookReported;

    /// <summary>Guards against opening two confirmation dialogs at once (NModalContainer only holds one).</summary>
    private static bool AnyModalOpen => NModalContainer.Instance?.OpenModal != null;

    internal static void Install(NRunHistory screen)
    {
        if (!RunHistoryAccess.IsAvailable)
        {
            Entry.Logger.Error(
                "run history internals were not found; the delete button will not be added. " +
                "The game build probably changed.");
            return;
        }

        if (screen.GetNodeOrNull<Control>(VisualName) is { } existing)
        {
            ResetVisual(existing, screen.GetNodeOrNull<Button>(HitboxName));
            return;
        }

        var packed = ResourceLoader.Load<PackedScene>(ScenePath);
        if (packed == null)
        {
            Entry.Logger.Error($"could not load '{ScenePath}'.");
            return;
        }

        var visual = packed.Instantiate<NDeleteProfileButton>(PackedScene.GenEditState.Disabled);
        if (visual == null)
        {
            Entry.Logger.Error($"could not instantiate '{ScenePath}' as NDeleteProfileButton.");
            return;
        }

        // The vanilla node must never receive input: its OnRelease deletes a profile.
        visual.Name = VisualName;
        visual.MouseFilter = Control.MouseFilterEnum.Ignore;
        visual.FocusMode = Control.FocusModeEnum.None;
        screen.AddChild(visual);
        Place(visual);
        ConfigureLabel(visual);

        var hitbox = new Button
        {
            Name = HitboxName,
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };

        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            hitbox.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        }

        hitbox.MouseEntered += () => PlayHover(visual, hovering: true);
        hitbox.MouseExited += () => PlayHover(visual, hovering: false);
        hitbox.FocusEntered += () => PlayHover(visual, hovering: true);
        hitbox.FocusExited += () => PlayHover(visual, hovering: false);
        hitbox.Pressed += () => TaskHelper.RunSafely(ConfirmAndDelete(screen, hitbox, visual));

        screen.AddChild(hitbox);
        Place(hitbox);

        Entry.Logger.Info(
            $"delete button added (rect={visual.GetGlobalRect()}, label={(OnFocusMethod != null ? "vanilla hover animation" : "fallback")}).");
    }

    /// <summary>Called every time the screen is (re)opened, including when a cached instance is reused.</summary>
    internal static void ResetVisual(NRunHistory screen)
    {
        ResetVisual(screen.GetNodeOrNull<Control>(VisualName), screen.GetNodeOrNull<Button>(HitboxName));
    }

    private static void ResetVisual(Control? visual, Button? hitbox)
    {
        if (hitbox != null)
        {
            hitbox.Disabled = false;
        }

        if (visual is not NDeleteProfileButton button)
        {
            return;
        }

        button.Scale = Vector2.One;
        if (LabelOf(button) is { } label)
        {
            label.SetTextAutoSize(UiText.Delete);
            SetLabelAlpha(label, 0f);
        }
    }

    /// <summary>
    /// Shows the vanilla yes/no popup, then deletes the run currently on screen if the player confirms.
    /// </summary>
    private static async Task ConfirmAndDelete(NRunHistory screen, Button hitbox, NDeleteProfileButton visual)
    {
        if (!GodotObject.IsInstanceValid(screen) || !GodotObject.IsInstanceValid(hitbox))
        {
            return;
        }

        var modalContainer = NModalContainer.Instance;
        if (modalContainer == null)
        {
            Entry.Logger.Error("NModalContainer.Instance is null; cannot show the confirmation.");
            return;
        }

        if (AnyModalOpen)
        {
            Entry.Logger.Warn("another modal is already open; not stacking a confirmation on it.");
            return;
        }

        if (!ModLoc.TryCreateConfirmStrings(out var header, out var body, out var locError))
        {
            Entry.Logger.Error($"could not build the confirmation text: {locError}");
            return;
        }

        var popup = NGenericPopup.Create();
        if (popup == null)
        {
            Entry.Logger.Warn("NGenericPopup.Create() returned null (test mode?); nothing shown.");
            return;
        }

        var confirmed = false;
        hitbox.Disabled = true;
        try
        {
            modalContainer.Add(popup);
            confirmed = await popup.WaitForConfirmation(
                body,
                header,
                new LocString(ModLoc.Table, ModLoc.CancelKey),
                new LocString(ModLoc.Table, ModLoc.ConfirmKey));
        }
        catch (Exception ex)
        {
            Entry.Logger.Error($"confirmation popup failed: {ex.GetBaseException().Message}");
            return;
        }
        finally
        {
            if (GodotObject.IsInstanceValid(hitbox))
            {
                hitbox.Disabled = false;
            }
        }

        if (!confirmed)
        {
            Entry.Logger.Info("deletion cancelled.");
            return;
        }

        if (!GodotObject.IsInstanceValid(screen) || !GodotObject.IsInstanceValid(visual))
        {
            Entry.Logger.Warn("screen went away while the confirmation was open.");
            return;
        }

        DeleteCurrentRun(screen, hitbox, visual);
    }

    private static void DeleteCurrentRun(NRunHistory screen, Button hitbox, NDeleteProfileButton visual)
    {
        var runNames = RunHistoryAccess.GetRunNames(screen);
        var index = RunHistoryAccess.GetIndex(screen);
        if (runNames == null || index < 0 || index >= runNames.Count)
        {
            Entry.Logger.Warn(
                $"no deletable run at cursor {index} (count={runNames?.Count.ToString() ?? "null"}); nothing to do.");
            ResetVisual(visual, hitbox);
            return;
        }

        var fileName = runNames[index];
        if (!RunHistoryDeleteService.TryDelete(fileName, out var detail))
        {
            Entry.Logger.Error($"failed to delete '{fileName}': {detail}");
            FlashLabel(screen, hitbox, visual, UiText.Failed);
            return;
        }

        Entry.Logger.Info(detail);

        // Keep the screen's in-memory list and cursor consistent with the files on disk.
        runNames.RemoveAt(index);
        if (runNames.Count == 0)
        {
            ResetVisual(visual, hitbox);
            CloseHistoryScreen(screen);
            return;
        }

        var nextIndex = Math.Min(index, runNames.Count - 1);
        RunHistoryAccess.SetIndex(screen, nextIndex);
        RunHistoryAccess.RefreshAndSelect(screen, nextIndex);

        FlashLabel(screen, hitbox, visual, UiText.Deleted);
    }

    /// <summary>
    /// Deleting the last record leaves a screen that would throw in <c>OnSubmenuShown</c>
    /// ("Tried to show run history screen with no runs!"), so close it and refresh the compendium entry
    /// that opened it.
    /// </summary>
    private static void CloseHistoryScreen(NRunHistory screen)
    {
        var stack = RunHistoryAccess.GetStack(screen);
        if (stack == null)
        {
            Entry.Logger.Warn("submenu stack unavailable; leaving the empty history screen open.");
            return;
        }

        stack.Pop();

        // NCompendiumSubmenu only sets RunHistoryButton.Visible once in _Ready, so hide it ourselves.
        if (stack.Peek()?.GetNodeOrNull<Control>("%RunHistoryButton") is { } runHistoryButton)
        {
            runHistoryButton.Visible = NRunHistory.CanBeShown();
        }

        Entry.Logger.Info("last history entry removed; closed the run history screen.");
    }

    /// <summary>Shows <paramref name="text"/> in the label for a moment, then restores the default text.</summary>
    private static void FlashLabel(NRunHistory screen, Button hitbox, NDeleteProfileButton visual, string text)
    {
        if (LabelOf(visual) is not { } label)
        {
            return;
        }

        label.SetTextAutoSize(text);
        SetLabelAlpha(label, 1f);

        var timer = screen.GetTree()?.CreateTimer(1.5);
        if (timer == null)
        {
            ResetVisual(visual, hitbox);
            return;
        }

        timer.Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(visual) || !GodotObject.IsInstanceValid(label))
            {
                return;
            }

            label.SetTextAutoSize(UiText.Delete);
            // Keep it visible if the pointer is still on the button, otherwise fade back out.
            if (!GodotObject.IsInstanceValid(hitbox) || !IsPointerOver(hitbox))
            {
                SetLabelAlpha(label, 0f);
            }
        };
    }

    /// <summary>Replays the vanilla hover/unhover animation of the profile delete button.</summary>
    private static void PlayHover(NDeleteProfileButton visual, bool hovering)
    {
        if (!GodotObject.IsInstanceValid(visual))
        {
            return;
        }

        var hook = hovering ? OnFocusMethod : OnUnfocusMethod;
        if (hook == null)
        {
            if (!_missingHoverHookReported)
            {
                _missingHoverHookReported = true;
                Entry.Logger.Warn("NDeleteProfileButton.OnFocus/OnUnfocus not found; using the simple fallback.");
            }

            if (LabelOf(visual) is { } label)
            {
                SetLabelAlpha(label, hovering ? 1f : 0f);
            }

            return;
        }

        try
        {
            hook.Invoke(visual, null);
        }
        catch (Exception ex)
        {
            Entry.Logger.Warn($"hover animation failed: {ex.GetBaseException().Message}");
        }
    }

    private static MegaLabel? LabelOf(NDeleteProfileButton visual)
    {
        return visual.GetNodeOrNull<MegaLabel>("%MegaLabel");
    }

    private static void SetLabelAlpha(MegaLabel label, float alpha)
    {
        var color = label.Modulate;
        color.A = alpha;
        label.Modulate = color;
    }

    private static bool IsPointerOver(Control control)
    {
        return control.GetGlobalRect().HasPoint(control.GetGlobalMousePosition());
    }

    /// <summary>
    /// Re-shapes the vanilla label: the scene's label is centred and 420px wide, which would run off the
    /// right edge of the screen for a button pinned to the corner. Keep the same size and the same
    /// vertical fly-out, but right-align the text against the icon.
    /// </summary>
    private static void ConfigureLabel(NDeleteProfileButton visual)
    {
        if (visual.GetNodeOrNull<Control>("Icon") is { } icon)
        {
            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        var label = LabelOf(visual);
        if (label == null)
        {
            Entry.Logger.Warn("the delete button scene has no %MegaLabel; hover text will be missing.");
            return;
        }

        label.MouseFilter = Control.MouseFilterEnum.Ignore;
        label.HorizontalAlignment = HorizontalAlignment.Right;
        label.AnchorLeft = 0f;
        label.AnchorRight = 1f;
        label.OffsetLeft = LabelLeft;
        label.OffsetRight = 0f;
        label.AnchorTop = 0.5f;
        label.AnchorBottom = 0.5f;
        label.OffsetTop = LabelTop;
        label.OffsetBottom = LabelBottom;
        label.SetTextAutoSize(UiText.Delete);
        SetLabelAlpha(label, 0f);
    }

    private static void Place(Control control)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.BottomRight, keepOffsets: false);
        control.OffsetLeft = OffsetLeft;
        control.OffsetTop = OffsetTop;
        control.OffsetRight = OffsetRight;
        control.OffsetBottom = OffsetBottom;
    }
}
