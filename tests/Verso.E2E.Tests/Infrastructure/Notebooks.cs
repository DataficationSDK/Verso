namespace Verso.E2E.Tests.Infrastructure;

/// <summary>Small notebooks the tests open.</summary>
public static class Notebooks
{
    /// <summary>A Jupyter notebook with one Markdown cell and one C# cell.</summary>
    public const string TwoCellJupyter = """
        {
         "cells": [
          { "cell_type": "markdown", "metadata": {}, "source": ["# From Jupyter"] },
          { "cell_type": "code", "execution_count": null, "metadata": {}, "outputs": [], "source": ["1 + 1"] }
         ],
         "metadata": {
          "kernelspec": { "display_name": ".NET (C#)", "language": "C#", "name": ".net-csharp" }
         },
         "nbformat": 4,
         "nbformat_minor": 5
        }
        """;

    /// <summary>
    /// A .verso notebook with one C# cell per entry, each holding the given number of short
    /// lines, plus a final two-line cell whose first line is far wider than any editor.
    /// </summary>
    public static string VersoWithCellsOf(params int[] lineCounts)
        => Verso(lineCounts
            .Select(count => (Guid.NewGuid(), Lines(count, "a")))
            .Append((Guid.NewGuid(), WideLine + "\nvar after = 1;")));

    /// <summary>A .verso notebook with one C# cell per source.</summary>
    public static string VersoWithSources(params string[] sources)
        => Verso(sources.Select(source => (Guid.NewGuid(), source)));

    /// <summary>A line far wider than any editor.</summary>
    public static string WideLine => "var wide = \"" + new string('x', 400) + "\";";

    /// <summary>A .verso notebook with a single C# cell.</summary>
    public static string VersoWithCell(Guid id, string source) => Verso([(id, source)]);

    /// <summary>The given number of short C# lines, each declaring a variable named with the prefix.</summary>
    public static string Lines(int count, string prefix)
        => string.Join("\n", Enumerable.Range(1, count).Select(i => $"var {prefix}{i} = {i};"));

    private static string Verso(IEnumerable<(Guid Id, string Source)> cells)
        => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["verso"] = "1.0",
            ["metadata"] = new Dictionary<string, object> { ["defaultKernel"] = "csharp" },
            ["cells"] = cells.Select(c => new Dictionary<string, object>
            {
                ["id"] = c.Id.ToString(),
                ["type"] = "code",
                ["language"] = "csharp",
                ["source"] = c.Source
            })
        });
}
