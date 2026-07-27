using System.Collections.Concurrent;
using System.Reflection;

namespace Verso.Python.Host;

/// <summary>
/// Decides which values describe the running program rather than data, and so have nothing
/// meaningful to say on the other side of the boundary.
/// </summary>
internal static class VariableShapes
{
    /// <summary>
    /// F# marks its compiled shapes with this attribute. It is read by name so that recognizing an
    /// F# value costs no reference to FSharp.Core, which this assembly has no other reason to take.
    /// </summary>
    private const string CompilationMappingAttribute = "Microsoft.FSharp.Core.CompilationMappingAttribute";

    /// <summary>The <c>SourceConstructFlags</c> value F# writes for a union. A record carries 2.</summary>
    private const int SumType = 1;

    private static readonly ConcurrentDictionary<Type, string?> Reasons = new();
    private static readonly ConcurrentDictionary<Type, int?> ConstructFlags = new();

    /// <summary>
    /// The reason a value of this type cannot cross, or null when it can be attempted. Callers use
    /// the reason as the explanation shown in place of the value.
    /// </summary>
    public static string? RefusalReason(Type type) => Reasons.GetOrAdd(type, Classify);

    private static string? Classify(Type type)
    {
        if (typeof(Delegate).IsAssignableFrom(type))
            return "a function, which cannot be called from another process";

        if (typeof(Task).IsAssignableFrom(type) || type == typeof(CancellationToken))
            return "a running operation rather than a value";

        // Kept from the behavior this replaced, and worth keeping for a second reason: a live
        // connection is reached this way, and reading its properties to describe it can open or
        // query something. Preparing to run a cell must not have effects of its own.
        if (typeof(IAsyncDisposable).IsAssignableFrom(type))
            return "a resource handle rather than a value";

        if (typeof(Type).IsAssignableFrom(type)
            || typeof(MemberInfo).IsAssignableFrom(type)
            || typeof(Assembly).IsAssignableFrom(type)
            || typeof(Module).IsAssignableFrom(type))
        {
            return "a description of the program rather than data";
        }

        if (type == typeof(IntPtr) || type == typeof(UIntPtr))
            return "a memory address, which means nothing in another process";

        if (IsUnionCase(type))
            return "an F# union case, which has no JSON form that keeps its case name";

        return null;
    }

    /// <summary>
    /// Whether a type is one case of a multi-case F# union.
    /// <para>
    /// The test deliberately requires the union marker on both the type and the type declaring it,
    /// because that combination is unique to a case nested inside its own union. F# list, option,
    /// and result carry the same marker on themselves but are declared at namespace level, and all
    /// three either convert correctly today or fail loudly on their own, so widening this test
    /// would refuse values that presently work.
    /// </para>
    /// <para>
    /// A case type reflects as its fields plus the compiler's own <c>Tag</c> and <c>Is</c> members,
    /// which serializes without error into something that looks like data but has lost the one
    /// thing that mattered, which case it was. Refusing it is better than that.
    /// </para>
    /// </summary>
    private static bool IsUnionCase(Type type)
        => SourceConstructFlags(type) == SumType
            && type.DeclaringType is { } declaring
            && SourceConstructFlags(declaring) == SumType;

    private static int? SourceConstructFlags(Type type) => ConstructFlags.GetOrAdd(type, static t =>
    {
        foreach (var attribute in t.GetCustomAttributes(inherit: true))
        {
            if (attribute.GetType().FullName != CompilationMappingAttribute)
                continue;

            var property = attribute.GetType().GetProperty("SourceConstructFlags");
            if (property?.GetValue(attribute) is { } value)
                return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        return null;
    });
}
