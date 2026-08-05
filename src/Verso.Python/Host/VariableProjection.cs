namespace Verso.Python.Host;

/// <summary>
/// One widget trait projected into the notebook's shared variables.
/// </summary>
/// <param name="Name">The shared variable other kernels read and write.</param>
/// <param name="Expression">The expression that named the object, as the author wrote it.</param>
/// <param name="Trait">The trait on that object.</param>
/// <param name="WidgetId">
/// What the projection is anchored to. A widget's model id is the name its view already addresses
/// it by, so a projection and a live view agree about which object they mean, and the same trait
/// bound a second time moves rather than doubling.
/// </param>
internal sealed record VariableProjection(
    string Name, string Expression, string Trait, string WidgetId);

/// <summary>
/// What came of asking for a projection to start or stop.
/// </summary>
/// <param name="Succeeded">Whether the projection is now in place, or was taken down.</param>
/// <param name="Reason">
/// Why not, written for the author of the cell. Every way this fails is something they can put
/// right: a name that is not in scope, an object with no such trait, a trait holding another
/// widget, or a value too large to keep sending.
/// </param>
/// <param name="Projection">The projection, when one was made.</param>
/// <param name="Replaced">The name of an earlier projection this one superseded, if any.</param>
internal sealed record ProjectionOutcome(
    bool Succeeded,
    string? Reason = null,
    VariableProjection? Projection = null,
    string? Replaced = null)
{
    public static ProjectionOutcome Failed(string reason) => new(false, reason);
}
