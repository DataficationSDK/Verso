namespace Verso.Abstractions;

/// <summary>
/// Provides shared variable storage for exchanging data between kernels within a notebook session.
/// </summary>
public interface IVariableStore
{
    /// <summary>
    /// Raised when the variable store contents change (after Set, Remove, or Clear).
    /// </summary>
    event Action? OnVariablesChanged;
    /// <summary>
    /// Stores a variable with the specified name, replacing any existing variable with the same name.
    /// </summary>
    /// <param name="name">The name of the variable.</param>
    /// <param name="value">The value to store.</param>
    void Set(string name, object value);

    /// <summary>
    /// Retrieves a variable by name, casting it to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected type of the variable.</typeparam>
    /// <param name="name">The name of the variable to retrieve.</param>
    /// <returns>The variable value cast to <typeparamref name="T"/>, or <c>default</c> if not found.</returns>
    T? Get<T>(string name);

    /// <summary>
    /// Attempts to retrieve a variable by name, casting it to the specified type.
    /// </summary>
    /// <typeparam name="T">The expected type of the variable.</typeparam>
    /// <param name="name">The name of the variable to retrieve.</param>
    /// <param name="value">When this method returns, contains the variable value if found; otherwise, <c>default</c>.</param>
    /// <returns><c>true</c> if the variable was found and successfully cast; otherwise, <c>false</c>.</returns>
    bool TryGet<T>(string name, out T? value);

    /// <summary>
    /// Returns descriptors for all variables currently stored.
    /// </summary>
    /// <returns>A read-only list of <see cref="VariableDescriptor"/> instances describing each stored variable.</returns>
    IReadOnlyList<VariableDescriptor> GetAll();

    /// <summary>
    /// Removes a variable by name.
    /// </summary>
    /// <param name="name">The name of the variable to remove.</param>
    /// <returns><c>true</c> if the variable was found and removed; otherwise, <c>false</c>.</returns>
    bool Remove(string name);

    /// <summary>
    /// Removes all variables from the store.
    /// </summary>
    void Clear();
}
