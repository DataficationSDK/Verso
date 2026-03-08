// ============================================================================
// This Perl kernel is dedicated to Brett Forsgren and the original .NET 
// Interactive team whose work laid the foundation for polyglot notebooks:
//
//   Diego Colombo, Luis Quintanilla, Jon Sequeira, and Giorgi Dalakishvili
//
// "The community feedback and contributions have shaped the polyglot notebook
//  and dotnet interactive architecture to what it is."  -- Diego Colombo
// ============================================================================

using System.Diagnostics;
using System.Text;
using Verso.Abstractions;

namespace Verso.Sample.Perl;

/// <summary>
/// Language kernel that executes Perl scripts via the system perl interpreter.
/// Each cell is written to a temporary .pl file and executed as a standalone script.
/// </summary>
[VersoExtension]
public sealed class PerlKernel : ILanguageKernel
{
    private string? _perlVersion;

    public string ExtensionId => "com.verso.sample.perl";
    public string Name => "Perl Kernel";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Executes Perl scripts via the system perl interpreter";
    public string LanguageId => "perl";
    public string DisplayName => "Perl";
    public IReadOnlyList<string> FileExtensions => new[] { ".pl", ".pm" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task InitializeAsync()
    {
        var psi = new ProcessStartInfo("perl", "-v")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(psi)!;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Extract version summary from the first non-empty parenthesized version line
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("This is perl", StringComparison.OrdinalIgnoreCase))
                {
                    _perlVersion = trimmed;
                    break;
                }
            }

            _perlVersion ??= "Perl (version unknown)";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Perl interpreter not found. Ensure 'perl' is installed and available on PATH.", ex);
        }
    }

    public async Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        var outputs = new List<CellOutput>();
        var tempFile = Path.GetTempFileName() + ".pl";

        try
        {
            await File.WriteAllTextAsync(tempFile, code, context.CancellationToken);

            var psi = new ProcessStartInfo("perl", tempFile)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;

            // Register cancellation to kill the process
            await using var registration = context.CancellationToken.Register(() =>
            {
                try { process.Kill(entireProcessTree: true); }
                catch { /* process may have already exited */ }
            });

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(context.CancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrEmpty(stdout))
                outputs.Add(new CellOutput("text/plain", stdout));

            if (!string.IsNullOrEmpty(stderr))
            {
                outputs.Add(new CellOutput("text/plain", stderr,
                    IsError: true,
                    ErrorName: process.ExitCode != 0 ? "PerlError" : "PerlWarning"));
            }

            if (outputs.Count == 0 && process.ExitCode == 0)
                outputs.Add(new CellOutput("text/plain", "(no output)"));
        }
        catch (OperationCanceledException)
        {
            outputs.Add(new CellOutput("text/plain", "Execution cancelled.", IsError: true));
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch { /* best effort cleanup */ }
        }

        return outputs;
    }

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var completions = new List<Completion>
        {
            new("use strict;", "use strict;\nuse warnings;\n", "Snippet", "Enable strict and warnings pragmas"),
            new("print", "print \"\";\n", "Snippet", "Print to standard output"),
            new("say", "say \"\";\n", "Snippet", "Print with automatic newline (requires use feature 'say')"),
            new("my $var", "my $", "Snippet", "Declare a lexical scalar variable"),
            new("my @array", "my @", "Snippet", "Declare a lexical array variable"),
            new("my %hash", "my %", "Snippet", "Declare a lexical hash variable"),
            new("foreach", "foreach my $item (@list) {\n    \n}\n", "Snippet", "Iterate over a list"),
            new("sub", "sub name {\n    my (@args) = @_;\n    \n}\n", "Snippet", "Define a subroutine"),
            new("if/else", "if (condition) {\n    \n} else {\n    \n}\n", "Snippet", "Conditional block"),
            new("open file", "open(my $fh, '<', $filename) or die \"Cannot open: $!\";\n", "Snippet", "Open a file for reading")
        };

        return Task.FromResult<IReadOnlyList<Completion>>(completions);
    }

    public async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var diagnostics = new List<Diagnostic>();
        var tempFile = Path.GetTempFileName() + ".pl";

        try
        {
            await File.WriteAllTextAsync(tempFile, code);

            var psi = new ProcessStartInfo("perl", $"-c \"{tempFile}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                // Parse perl -c error output: errors typically show "at <file> line <N>"
                var lines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    // Skip the "syntax OK" or temp filename noise
                    if (trimmed.EndsWith("syntax OK", StringComparison.OrdinalIgnoreCase)) continue;

                    var lineNum = 0;
                    var atLineIdx = trimmed.LastIndexOf(" line ", StringComparison.OrdinalIgnoreCase);
                    if (atLineIdx >= 0)
                    {
                        var afterLine = trimmed[(atLineIdx + 6)..];
                        var numEnd = 0;
                        while (numEnd < afterLine.Length && char.IsDigit(afterLine[numEnd]))
                            numEnd++;
                        if (numEnd > 0)
                            int.TryParse(afterLine[..numEnd], out lineNum);
                    }

                    // Perl line numbers are 1-based; Diagnostic expects 0-based
                    var diagLine = Math.Max(0, lineNum - 1);

                    // Clean the message by removing the temp file path
                    var message = trimmed.Replace(tempFile, "<script>");

                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        message,
                        diagLine, 0, diagLine, 0));
                }
            }
        }
        catch
        {
            // If perl is not available, skip diagnostics gracefully
        }
        finally
        {
            try { File.Delete(tempFile); }
            catch { /* best effort cleanup */ }
        }

        return diagnostics;
    }

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        if (_perlVersion is not null)
            return Task.FromResult<HoverInfo?>(new HoverInfo($"**Perl Kernel**\n\n{_perlVersion}"));

        return Task.FromResult<HoverInfo?>(null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
