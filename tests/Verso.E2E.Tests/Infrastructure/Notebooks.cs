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
}
