using System.Text;
using Verso.Abstractions;
using Verso.Sample.Dice.Models;

namespace Verso.Sample.Dice;

/// <summary>
/// Language kernel that parses dice notation (e.g. "2d6+3") and returns roll results.
/// Each line of input is treated as a separate dice expression.
/// </summary>
[VersoExtension]
public sealed class DiceExtension : ILanguageKernel
{
    private static readonly Random Rng = new();

    public string ExtensionId => "com.verso.sample.dice";
    public string Name => "Dice Kernel";
    public string Version => "1.0.0";
    public string? Author => "Verso Contributors";
    public string? Description => "Rolls dice using standard notation (NdS+M)";
    public string LanguageId => "dice";
    public string DisplayName => "Dice";
    public IReadOnlyList<string> FileExtensions => new[] { ".dice" };

    public Task OnLoadedAsync(IExtensionHostContext context) => Task.CompletedTask;
    public Task OnUnloadedAsync() => Task.CompletedTask;
    public Task InitializeAsync() => Task.CompletedTask;

    public Task<IReadOnlyList<CellOutput>> ExecuteAsync(string code, IExecutionContext context)
    {
        var outputs = new List<CellOutput>();
        var lines = code.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var line in lines)
        {
            var notation = DiceNotation.TryParse(line);
            if (notation is null)
            {
                outputs.Add(new CellOutput("text/plain", $"Invalid dice notation: {line}", IsError: true));
                continue;
            }

            var rolls = new int[notation.Count];
            for (var i = 0; i < notation.Count; i++)
                rolls[i] = Rng.Next(1, notation.Sides + 1);

            var result = new DiceResult(notation, rolls);
            context.Variables.Set($"_lastRoll", result);

            var sb = new StringBuilder();
            sb.Append($"{notation} => [{string.Join(", ", rolls)}]");
            if (notation.Modifier != 0)
                sb.Append($" {(notation.Modifier > 0 ? "+" : "")}{notation.Modifier}");
            sb.Append($" = {result.Total}");

            outputs.Add(new CellOutput("text/plain", sb.ToString()));
        }

        return Task.FromResult<IReadOnlyList<CellOutput>>(outputs);
    }

    public Task<IReadOnlyList<Completion>> GetCompletionsAsync(string code, int cursorPosition)
    {
        var completions = new List<Completion>
        {
            new("1d20", "1d20", "Snippet", "Roll a twenty-sided die"),
            new("2d6", "2d6", "Snippet", "Roll two six-sided dice"),
            new("1d100", "1d100", "Snippet", "Roll a percentile die"),
            new("4d6", "4d6", "Snippet", "Roll four six-sided dice (ability score)")
        };
        return Task.FromResult<IReadOnlyList<Completion>>(completions);
    }

    public Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(string code)
    {
        var diagnostics = new List<Diagnostic>();
        var lines = code.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            if (DiceNotation.TryParse(line) is null)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Invalid dice notation: '{line}'. Use format NdS or NdS+M (e.g. 2d6+3)",
                    i, 0, i, line.Length));
            }
        }

        return Task.FromResult<IReadOnlyList<Diagnostic>>(diagnostics);
    }

    public Task<HoverInfo?> GetHoverInfoAsync(string code, int cursorPosition)
    {
        var lines = code.Split('\n');
        var pos = 0;
        foreach (var line in lines)
        {
            if (cursorPosition <= pos + line.Length)
            {
                var notation = DiceNotation.TryParse(line.Trim());
                if (notation is not null)
                {
                    var minRoll = notation.Count + notation.Modifier;
                    var maxRoll = notation.Count * notation.Sides + notation.Modifier;
                    var avg = (minRoll + maxRoll) / 2.0;
                    return Task.FromResult<HoverInfo?>(
                        new HoverInfo($"{notation}: min={minRoll}, max={maxRoll}, avg={avg:F1}"));
                }
                break;
            }
            pos += line.Length + 1;
        }
        return Task.FromResult<HoverInfo?>(null);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
