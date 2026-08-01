namespace Verso.Abstractions;

/// <summary>
/// Represents an action that can appear on the notebook toolbar or in context menus.
/// Actions expose a command that the user can trigger, with optional enable/disable logic.
/// </summary>
public interface IToolbarAction : IExtension
{
    /// <summary>
    /// Unique identifier for this action (e.g. "run-all", "export-pdf").
    /// </summary>
    string ActionId { get; }

    /// <summary>
    /// Human-readable label displayed on the toolbar button or menu item.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Optional icon name or path for the action button.
    /// </summary>
    string? Icon { get; }

    /// <summary>
    /// When <c>true</c>, the toolbar renders only the icon and surfaces the
    /// <see cref="DisplayName"/> as a hover tooltip instead of a visible label.
    /// Hosts ignore this for actions without an <see cref="Icon"/>. Defaults to
    /// <c>false</c> so the label is shown.
    /// </summary>
    /// <remarks>
    /// An icon-only button is worth explaining. Hosts show
    /// <see cref="IExtension.Description"/> beneath the name in the button's tooltip,
    /// which is the only chance to say what the icon means.
    /// </remarks>
    bool IconOnly => false;

    /// <summary>
    /// When <c>true</c>, the toolbar gives this action a filled, accent-colored
    /// "primary" appearance to mark it as the prominent call to action. Defaults
    /// to <c>false</c> for the standard flat button style.
    /// </summary>
    bool IsPrimary => false;

    /// <summary>
    /// Optional prompt asking the user to confirm before the action runs. When non-null,
    /// hosts show a confirmation dialog with this text and execute the action only if the
    /// user accepts. Intended for destructive actions whose effect cannot be undone (e.g.
    /// restarting a kernel, which discards session state). Defaults to <c>null</c> so the
    /// action executes immediately on click.
    /// </summary>
    string? ConfirmationPrompt => null;

    /// <summary>
    /// Specifies where the action should appear (e.g. main toolbar, cell toolbar, context menu).
    /// </summary>
    ToolbarPlacement Placement { get; }

    /// <summary>
    /// Sort order within the placement group. Lower values appear first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Determines whether the action is currently enabled based on the notebook state.
    /// </summary>
    /// <param name="context">Context providing access to the current notebook, selection, and services.</param>
    /// <returns><c>true</c> if the action should be enabled; otherwise <c>false</c>.</returns>
    Task<bool> IsEnabledAsync(IToolbarActionContext context);

    /// <summary>
    /// Executes the action.
    /// </summary>
    /// <param name="context">Context providing access to the current notebook, selection, and services.</param>
    /// <returns>A task that completes when the action has finished executing.</returns>
    Task ExecuteAsync(IToolbarActionContext context);
}
