using System.Text.RegularExpressions;

namespace Verso.Sample.Dice.Models;

/// <summary>
/// Parses dice notation strings like "2d6", "1d20+5", "3d8-2".
/// Format: [count]d[sides][+/-modifier]
/// </summary>
public sealed class DiceNotation
{
    private static readonly Regex Pattern = new(
        @"^(?<count>\d+)d(?<sides>\d+)(?<sign>[+-](?<mod>\d+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public int Count { get; }
    public int Sides { get; }
    public int Modifier { get; }

    private DiceNotation(int count, int sides, int modifier)
    {
        Count = count;
        Sides = sides;
        Modifier = modifier;
    }

    /// <summary>
    /// Attempts to parse a dice notation string. Returns null if the format is invalid.
    /// </summary>
    public static DiceNotation? TryParse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var match = Pattern.Match(input.Trim());
        if (!match.Success)
            return null;

        var count = int.Parse(match.Groups["count"].Value);
        var sides = int.Parse(match.Groups["sides"].Value);

        if (count < 1 || count > 100 || sides < 1 || sides > 1000)
            return null;

        var modifier = 0;
        if (match.Groups["sign"].Success)
        {
            modifier = int.Parse(match.Groups["mod"].Value);
            if (match.Groups["sign"].Value.StartsWith('-'))
                modifier = -modifier;
        }

        return new DiceNotation(count, sides, modifier);
    }

    public override string ToString()
    {
        var mod = Modifier switch
        {
            > 0 => $"+{Modifier}",
            < 0 => Modifier.ToString(),
            _ => ""
        };
        return $"{Count}d{Sides}{mod}";
    }
}
