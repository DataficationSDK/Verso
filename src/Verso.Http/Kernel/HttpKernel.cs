using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Verso.Abstractions;
using Verso.Http.Formatting;
using Verso.Http.Models;
using Verso.Http.Parsing;

namespace Verso.Http.Kernel;

/// <summary>
/// Language kernel for executing HTTP requests in .http file syntax.
/// Accessed through <see cref="CellType.HttpCellType"/>; not independently registered.
/// </summary>
public sealed class HttpKernel : ILanguageKernel
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromSeconds(300) // Outer timeout; per-request timeout via CTS
    };

    private static readonly HttpClient NoRedirectClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(300)
    };

    private static readonly Regex UnresolvedVarPattern = new(
        @"\{\{(.+?)\}\}", RegexOptions.Compiled);

    internal const string BaseUrlStoreKey = "__verso_http_base_url";
    internal const string DefaultHeadersStoreKey = "__verso_http_default_headers";
    internal const string TimeoutStoreKey = "__verso_http_timeout";

    private readonly Dictionary<string, HttpResponseData> _namedResponses = new(StringComparer.OrdinalIgnoreCase);
    private IVariableStore? _lastVariableStore;

    // --- IExtension ---
    public string ExtensionId => "verso.http.kernel.http";
    string IExtension.Name => "HTTP Kernel";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Executes HTTP requests using .http file syntax.";

    // --- ILanguageKernel ---
    public string LanguageId => "http";
    public string DisplayName => "HTTP";
    public IReadOnlyList<string> FileExtensions { get; } = new[] { ".http", ".rest" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        _lastVariableStore = context.Variables;
        var outputs = new List<CellOutput>();

        var (variables, requests) = HttpRequestParser.Parse(code);

        if (requests.Count == 0)
        {
            outputs.Add(new CellOutput("text/plain", "No HTTP request found.", IsError: true));
            return outputs;
        }

        var resolver = new HttpVariableResolver(variables, context.Variables, _namedResponses);

        // Read configuration from variable store
        context.Variables.TryGet<string>(BaseUrlStoreKey, out var baseUrl);
        context.Variables.TryGet<Dictionary<string, string>>(DefaultHeadersStoreKey, out var defaultHeaders);
        context.Variables.TryGet<int>(TimeoutStoreKey, out var timeoutSeconds);
        if (timeoutSeconds <= 0) timeoutSeconds = 30;

        foreach (var request in requests)
        {
            // Resolve variables
            var url = resolver.Resolve(request.Url);
            var resolvedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Apply default headers first
            if (defaultHeaders is not null)
            {
                foreach (var (key, value) in defaultHeaders)
                    resolvedHeaders[key] = resolver.Resolve(value);
            }

            // Request headers override defaults
            foreach (var (key, value) in request.Headers)
                resolvedHeaders[key] = resolver.Resolve(value);

            var body = request.Body is not null ? resolver.Resolve(request.Body) : null;

            // Handle relative URLs
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    url = baseUrl.TrimEnd('/') + "/" + url.TrimStart('/');
                }
                else
                {
                    outputs.Add(new CellOutput("text/plain",
                        $"Error: Relative URL '{url}' requires a base URL. Use #!http-set-base.", IsError: true));
                    continue;
                }
            }

            // Build HttpRequestMessage
            var httpMethod = new HttpMethod(request.Method);
            var requestMsg = new HttpRequestMessage(httpMethod, url);

            // Set body
            if (body is not null)
            {
                var contentType = resolvedHeaders.TryGetValue("Content-Type", out var ct) ? ct : "application/json";
                requestMsg.Content = new StringContent(body, Encoding.UTF8);
                requestMsg.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                resolvedHeaders.Remove("Content-Type");
            }

            // Set headers
            foreach (var (key, value) in resolvedHeaders)
            {
                if (!requestMsg.Headers.TryAddWithoutValidation(key, value))
                    requestMsg.Content?.Headers.TryAddWithoutValidation(key, value);
            }

            // Stream status message
            await context.WriteOutputAsync(new CellOutput("text/plain",
                $"Sending {request.Method} {url}...")).ConfigureAwait(false);

            // Send request
            var client = request.NoRedirect ? NoRedirectClient : SharedClient;
            var sw = Stopwatch.StartNew();

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                using var response = await client.SendAsync(requestMsg, cts.Token).ConfigureAwait(false);
                sw.Stop();

                var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                var responseData = new HttpResponseData
                {
                    StatusCode = (int)response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase,
                    Body = responseBody,
                    ContentType = response.Content.Headers.ContentType?.ToString(),
                    ElapsedMs = sw.ElapsedMilliseconds
                };

                foreach (var header in response.Headers)
                    responseData.Headers[header.Key] = string.Join(", ", header.Value);
                foreach (var header in response.Content.Headers)
                    responseData.Headers[header.Key] = string.Join(", ", header.Value);

                // Store named response
                if (request.Name is not null)
                    _namedResponses[request.Name] = responseData;

                // Format and add output
                var html = HttpResponseFormatter.FormatResponseHtml(responseData);
                outputs.Add(new CellOutput("text/html", html));

                // Store last response in variable store
                context.Variables.Set("httpResponse", responseBody);
                context.Variables.Set("httpStatus", responseData.StatusCode);
            }
            catch (TaskCanceledException) when (!context.CancellationToken.IsCancellationRequested)
            {
                sw.Stop();
                outputs.Add(new CellOutput("text/plain",
                    $"Error: Request timed out after {timeoutSeconds}s.", IsError: true));
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                outputs.Add(new CellOutput("text/plain", "Request cancelled.", IsError: true));
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                outputs.Add(new CellOutput("text/plain",
                    $"Error: {ex.Message}", IsError: true));
            }
        }

        return outputs;
    }

    // --- Completions ---

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var completions = new List<Completion>();
        var partial = ExtractPartialWord(code, cursorPosition);

        // Check if inside {{ }} — offer variables
        var beforeCursor = code.Substring(0, Math.Min(cursorPosition, code.Length));
        if (beforeCursor.LastIndexOf("{{", StringComparison.Ordinal) > beforeCursor.LastIndexOf("}}", StringComparison.Ordinal))
        {
            // Variable completions
            if (_lastVariableStore is not null)
            {
                var varPartial = ExtractPartialAfterDoubleBrace(beforeCursor);
                foreach (var v in _lastVariableStore.GetAll())
                {
                    if (v.Name.StartsWith("__verso_", StringComparison.Ordinal))
                        continue;
                    if (MatchesPrefix(v.Name, varPartial))
                    {
                        completions.Add(new Completion(
                            v.Name, v.Name, "Variable",
                            $"{v.Type.Name}: {TruncateValue(v.Value)}",
                            $"0_{v.Name}"));
                    }
                }
            }

            // Dynamic variable completions
            foreach (var (name, desc) in DynamicVariables)
            {
                var varPartial = ExtractPartialAfterDoubleBrace(beforeCursor);
                if (MatchesPrefix(name, varPartial))
                {
                    completions.Add(new Completion(name, name, "Function", desc, $"1_{name}"));
                }
            }

            return Task.FromResult<IReadOnlyList<Completion>>(completions);
        }

        // HTTP methods
        foreach (var method in HttpMethods)
        {
            if (MatchesPrefix(method, partial))
                completions.Add(new Completion(method, method, "Keyword",
                    HttpMethodDescriptions.TryGetValue(method, out var d) ? d : null,
                    $"0_{method}"));
        }

        // Common headers
        foreach (var header in CommonHeaders)
        {
            if (MatchesPrefix(header, partial))
                completions.Add(new Completion(header + ": ", header + ": ", "Property",
                    HeaderDescriptions.TryGetValue(header, out var d) ? d : null,
                    $"1_{header}"));
        }

        return Task.FromResult<IReadOnlyList<Completion>>(completions);
    }

    // --- Diagnostics ---

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var diagnostics = new List<Diagnostic>();
        var (variables, requests) = HttpRequestParser.Parse(code);

        if (requests.Count == 0 && variables.Count == 0 && !string.IsNullOrWhiteSpace(code))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Error,
                "No valid HTTP request found. Start with a method (GET, POST, etc.) followed by a URL.",
                0, 0, 0, 0));
        }

        foreach (var request in requests)
        {
            if (string.IsNullOrWhiteSpace(request.Url))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error, "Missing URL.", 0, 0, 0, 0));
            }

            // Warn about unrecognized methods
            if (!RecognizedMethods.Contains(request.Method))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Unrecognized HTTP method: {request.Method}",
                    0, 0, 0, 0));
            }
        }

        // Check for unresolved variables
        var matches = UnresolvedVarPattern.Matches(code);
        var fileVarNames = new HashSet<string>(
            variables.Select(v => v.Name), StringComparer.OrdinalIgnoreCase);

        foreach (Match match in matches)
        {
            var expr = match.Groups[1].Value.Trim();

            // Skip dynamic variables and response references
            if (expr.StartsWith('$'))
                continue;
            if (expr.Contains(".response.", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip file-level variables
            if (fileVarNames.Contains(expr))
                continue;

            // Check variable store
            bool resolved = false;
            if (_lastVariableStore is not null)
            {
                resolved = _lastVariableStore.TryGet<object>(expr, out _);
            }

            if (!resolved)
            {
                var (line, col) = OffsetToLineCol(code, match.Index);
                var (endLine, endCol) = OffsetToLineCol(code, match.Index + match.Length);
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Unresolved variable '{{{{{expr}}}}}'.",
                    line, col, endLine, endCol));
            }
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
    }

    // --- Hover ---

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        var (word, wordStart, wordEnd) = ExtractWordAtCursor(code, cursorPosition);
        if (string.IsNullOrEmpty(word))
            return Task.FromResult<HoverInfo?>(null);

        var (startLine, startCol) = OffsetToLineCol(code, wordStart);
        var (endLine, endCol) = OffsetToLineCol(code, wordEnd);
        var range = (startLine, startCol, endLine, endCol);

        if (HttpMethodDescriptions.TryGetValue(word.ToUpperInvariant(), out var methodDesc))
            return Task.FromResult<HoverInfo?>(new HoverInfo(methodDesc, "text/plain", range));

        if (HeaderDescriptions.TryGetValue(word, out var headerDesc))
            return Task.FromResult<HoverInfo?>(new HoverInfo(headerDesc, "text/plain", range));

        // Variable hover
        if (_lastVariableStore is not null)
        {
            var allVars = _lastVariableStore.GetAll();
            var descriptor = allVars.FirstOrDefault(v =>
                string.Equals(v.Name, word, StringComparison.OrdinalIgnoreCase));
            if (descriptor is not null)
            {
                var content = $"Variable: {descriptor.Name}\nType: {descriptor.Type.Name}\nValue: {TruncateValue(descriptor.Value)}";
                return Task.FromResult<HoverInfo?>(new HoverInfo(content, "text/plain", range));
            }
        }

        return Task.FromResult<HoverInfo?>(null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // --- Private helpers ---

    private static string ExtractPartialWord(string code, int cursorPosition)
    {
        if (cursorPosition <= 0 || cursorPosition > code.Length)
            return "";

        int start = cursorPosition - 1;
        while (start >= 0 && (char.IsLetterOrDigit(code[start]) || code[start] == '-' || code[start] == '_'))
            start--;

        start++;
        return code.Substring(start, cursorPosition - start);
    }

    private static string ExtractPartialAfterDoubleBrace(string beforeCursor)
    {
        int idx = beforeCursor.LastIndexOf("{{", StringComparison.Ordinal);
        if (idx < 0) return "";
        return beforeCursor.Substring(idx + 2);
    }

    private static (string Word, int Start, int End) ExtractWordAtCursor(string code, int cursorPosition)
    {
        if (cursorPosition < 0 || cursorPosition > code.Length || code.Length == 0)
            return ("", 0, 0);

        int pos = cursorPosition < code.Length ? cursorPosition : cursorPosition - 1;
        if (pos < 0 || !IsWordChar(code[pos]))
        {
            pos = cursorPosition - 1;
            if (pos < 0 || !IsWordChar(code[pos]))
                return ("", 0, 0);
        }

        int start = pos;
        int end = pos;
        while (start > 0 && IsWordChar(code[start - 1])) start--;
        while (end < code.Length - 1 && IsWordChar(code[end + 1])) end++;

        return (code.Substring(start, end - start + 1), start, end + 1);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_';

    private static bool MatchesPrefix(string candidate, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return true;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static (int Line, int Column) OffsetToLineCol(string text, int offset)
    {
        int line = 0, col = 0;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') { line++; col = 0; } else { col++; }
        }
        return (line, col);
    }

    private static string TruncateValue(object? value, int maxLength = 100)
    {
        if (value is null) return "null";
        var str = value.ToString() ?? "null";
        return str.Length > maxLength ? str.Substring(0, maxLength) + "..." : str;
    }

    private static readonly HashSet<string> RecognizedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT"
    };

    private static readonly string[] HttpMethods =
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"
    };

    private static readonly Dictionary<string, string> HttpMethodDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GET"] = "GET — Retrieve a resource. Should not have side effects.",
        ["POST"] = "POST — Submit data to create or process a resource.",
        ["PUT"] = "PUT — Replace a resource entirely.",
        ["PATCH"] = "PATCH — Partially update a resource.",
        ["DELETE"] = "DELETE — Remove a resource.",
        ["HEAD"] = "HEAD — Like GET but returns only headers, no body.",
        ["OPTIONS"] = "OPTIONS — Describe communication options for the target resource.",
    };

    private static readonly string[] CommonHeaders =
    {
        "Content-Type", "Authorization", "Accept", "Cache-Control",
        "User-Agent", "Cookie", "X-Request-Id", "If-None-Match",
        "If-Modified-Since", "Accept-Encoding", "Accept-Language"
    };

    private static readonly Dictionary<string, string> HeaderDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Content-Type"] = "Content-Type — The media type of the request body (e.g., application/json).",
        ["Authorization"] = "Authorization — Credentials for authenticating the request (e.g., Bearer token).",
        ["Accept"] = "Accept — Media types the client can handle in the response.",
        ["Cache-Control"] = "Cache-Control — Directives for caching mechanisms.",
        ["User-Agent"] = "User-Agent — Identifies the client software making the request.",
        ["Cookie"] = "Cookie — HTTP cookies previously sent by the server.",
        ["X-Request-Id"] = "X-Request-Id — Custom header for request tracing.",
        ["If-None-Match"] = "If-None-Match — Conditional request using ETag values.",
        ["If-Modified-Since"] = "If-Modified-Since — Conditional request based on last modification date.",
        ["Accept-Encoding"] = "Accept-Encoding — Acceptable content encoding (e.g., gzip, deflate).",
        ["Accept-Language"] = "Accept-Language — Preferred natural languages for the response.",
    };

    private static readonly (string Name, string Description)[] DynamicVariables =
    {
        ("$guid", "Generate a new UUID/GUID."),
        ("$randomInt", "Generate a random integer. Usage: $randomInt [min max]"),
        ("$timestamp", "Current Unix timestamp in seconds. Usage: $timestamp [offset unit]"),
        ("$datetime", "Current UTC datetime. Usage: $datetime [format] [offset unit]"),
        ("$localDatetime", "Current local datetime. Usage: $localDatetime [format] [offset unit]"),
        ("$processEnv", "Read an environment variable. Usage: $processEnv NAME"),
    };
}
