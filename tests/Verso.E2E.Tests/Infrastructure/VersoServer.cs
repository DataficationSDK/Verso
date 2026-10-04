using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace Verso.E2E.Tests.Infrastructure;

/// <summary>
/// A <c>verso serve</c> process started from the CLI this suite was built against, on a free
/// port, over plain HTTP, with the interface pinned to English.
/// </summary>
public sealed class VersoServer : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

    private readonly Process _process;
    private readonly StringBuilder _log = new();

    private VersoServer(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
    }

    /// <summary>The root URL the server answers on, without a trailing slash.</summary>
    public string BaseUrl { get; }

    /// <summary>
    /// The URL that opens <paramref name="notebookPath"/>. Serve only opens a file named in the
    /// <c>recover</c> query parameter; the bare root shows the welcome screen.
    /// </summary>
    public string UrlFor(string notebookPath)
        => $"{BaseUrl}/?recover={Uri.EscapeDataString(Path.GetFullPath(notebookPath))}";

    /// <summary>What the server has written to standard output and error so far.</summary>
    public string Log
    {
        get { lock (_log) return _log.ToString(); }
    }

    /// <summary>Starts a server and returns once it answers HTTP requests.</summary>
    /// <param name="extraArguments">Further <c>verso serve</c> options, such as <c>--preserve-format</c>.</param>
    public static async Task<VersoServer> StartAsync(params string[] extraArguments)
    {
        var cliPath = ResolveCliPath();
        var port = FindFreePort();

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(cliPath)!,
        };
        foreach (var argument in new[]
                 {
                     cliPath, "serve",
                     "--no-browser", "--no-https",
                     "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                     "--language", "en",
                 }.Concat(extraArguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var server = new VersoServer(process, $"http://localhost:{port}");
        process.OutputDataReceived += (_, e) => server.Append(e.Data);
        process.ErrorDataReceived += (_, e) => server.Append(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await server.WaitUntilAnsweringAsync();
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }

        return server;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            }
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the kill.
        }
        catch (TimeoutException)
        {
            // Left to the operating system; nothing later in the run depends on this port.
        }
        finally
        {
            _process.Dispose();
        }
    }

    private void Append(string? line)
    {
        if (line is null) return;
        lock (_log) _log.AppendLine(line);
    }

    private async Task WaitUntilAnsweringAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + StartupTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"verso serve exited with code {_process.ExitCode} before answering.\n{Log}");
            }

            try
            {
                using var response = await http.GetAsync(BaseUrl + "/");
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Listening but slow to answer the first request.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"verso serve did not answer on {BaseUrl} within {StartupTimeout.TotalSeconds:0} s.\n{Log}");
    }

    private static string ResolveCliPath()
    {
        var path = Environment.GetEnvironmentVariable("VERSO_E2E_CLI");
        if (string.IsNullOrEmpty(path))
        {
            path = typeof(VersoServer).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "VersoCliPath")?.Value;
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "The Verso CLI was not found. Build the test project, which builds the CLI with it, "
                + "or point VERSO_E2E_CLI at a Verso.Cli.dll.", path);
        }

        return path;
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
