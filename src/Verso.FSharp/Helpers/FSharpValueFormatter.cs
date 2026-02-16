using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;

namespace Verso.FSharp.Helpers;

/// <summary>
/// Formats F# values for display and resolves async/Task values.
/// </summary>
internal static class FSharpValueFormatter
{
    /// <summary>
    /// Formats an object for display output. Uses <c>sprintf "%A"</c> style formatting
    /// for F# types when possible, falling back to <c>.ToString()</c>.
    /// </summary>
    public static string FormatValue(object? value)
    {
        if (value is null)
            return "null";

        // Unit type renders as empty
        if (value is Unit)
            return "";

        return value.ToString() ?? "";
    }

    /// <summary>
    /// Detects and awaits <see cref="Task{T}"/> and <see cref="FSharpAsync{T}"/> values.
    /// Returns the resolved value, or the original if not async.
    /// </summary>
    public static async Task<object?> ResolveAsyncValue(object? value, CancellationToken ct)
    {
        if (value is null)
            return null;

        // Handle Task (non-generic)
        if (value is Task task && value.GetType() == typeof(Task))
        {
            await task.WaitAsync(ct).ConfigureAwait(false);
            return null;
        }

        // Handle Task<T>
        if (value is Task taskGeneric)
        {
            var type = taskGeneric.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                await taskGeneric.WaitAsync(ct).ConfigureAwait(false);
                var resultProperty = type.GetProperty("Result");
                return resultProperty?.GetValue(taskGeneric);
            }
        }

        // Handle FSharpAsync<T> -- convert to Task and await
        var valueType = value.GetType();
        if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(FSharpAsync<>))
        {
            try
            {
                // FSharpAsync.StartAsTask(async, ?, cancellationToken)
                var elementType = valueType.GetGenericArguments()[0];
                var startAsTaskMethod = typeof(FSharpAsync)
                    .GetMethods()
                    .FirstOrDefault(m =>
                        m.Name == "StartAsTask" &&
                        m.GetParameters().Length == 3);

                if (startAsTaskMethod is not null)
                {
                    var genericMethod = startAsTaskMethod.MakeGenericMethod(elementType);
                    var resultTask = (Task)genericMethod.Invoke(null, new object?[]
                    {
                        value,
                        FSharpOption<TaskCreationOptions>.None,
                        FSharpOption<CancellationToken>.Some(ct)
                    })!;

                    await resultTask.WaitAsync(ct).ConfigureAwait(false);
                    var resultProp = resultTask.GetType().GetProperty("Result");
                    return resultProp?.GetValue(resultTask);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // If reflection-based async resolution fails, return original value
            }
        }

        return value;
    }
}
