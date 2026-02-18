namespace Verso.Sample.Dice.Models;

/// <summary>
/// Represents the result of rolling dice described by a <see cref="DiceNotation"/>.
/// </summary>
public sealed class DiceResult
{
    public DiceNotation Notation { get; }
    public IReadOnlyList<int> Rolls { get; }
    public int Modifier { get; }
    public int Total { get; }

    public DiceResult(DiceNotation notation, IReadOnlyList<int> rolls)
    {
        Notation = notation;
        Rolls = rolls;
        Modifier = notation.Modifier;
        Total = rolls.Sum() + notation.Modifier;
    }
}
