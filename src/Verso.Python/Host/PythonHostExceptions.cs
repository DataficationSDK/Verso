namespace Verso.Python.Host;

/// <summary>
/// Base type for failures raised while starting or supervising the out-of-process Python host.
/// </summary>
internal class PythonHostException : Exception
{
    public PythonHostException(string message) : base(message) { }

    public PythonHostException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Raised when the subprocess presents an incorrect or missing handshake token.
/// </summary>
internal sealed class PythonHostAuthenticationException : PythonHostException
{
    public PythonHostAuthenticationException(string message) : base(message) { }
}

/// <summary>
/// Raised when a wire frame violates the protocol contract, for example a frame larger than
/// the permitted ceiling, a malformed JSON payload, or a stream that ends part way through a frame.
/// </summary>
internal sealed class PythonHostProtocolException : PythonHostException
{
    public PythonHostProtocolException(string message) : base(message) { }
}

/// <summary>
/// Raised when the subprocess does not complete the handshake within the allotted time.
/// </summary>
internal sealed class PythonHostTimeoutException : PythonHostException
{
    public PythonHostTimeoutException(string message) : base(message) { }
}
