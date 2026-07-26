using Verso.Abstractions;

namespace Verso.Execution;

/// <summary>
/// Bounds how much output one cell execution may accumulate.
/// <para>
/// A cell that loops while printing, or one that logs for hours, would otherwise grow the notebook
/// without limit, and every block it produces is written to disk on save. When a cell reaches its
/// limit it stops accepting output and says so once, rather than quietly dropping the rest. The
/// cell itself is unaffected and keeps running to completion.
/// </para>
/// <para>
/// One budget belongs to one execution. Limits are deliberately far above what ordinary work
/// produces, so reaching one is a signal that something is wrong rather than a constraint to
/// design around.
/// </para>
/// </summary>
internal sealed class OutputBudget
{
    /// <summary>The most output blocks one cell may accumulate.</summary>
    internal const int MaxBlocks = 1_000;

    /// <summary>The most characters one cell's outputs may hold in total.</summary>
    internal const int MaxCharacters = 16 * 1024 * 1024;

    private int _characters;
    private string? _reason;
    private bool _reported;

    /// <summary>Whether this cell has stopped accepting output.</summary>
    public bool LimitReached => _reason is not null;

    /// <summary>
    /// Accounts for a block about to be added. Returns false when the cell is full, in which case
    /// the caller drops the output and asks <see cref="TryDescribeLimit"/> for the notice.
    /// </summary>
    /// <param name="currentBlockCount">How many blocks the cell already holds.</param>
    /// <param name="content">The content of the block being added.</param>
    public bool TryAddBlock(int currentBlockCount, string content)
    {
        if (_reason is not null)
            return false;

        if (currentBlockCount >= MaxBlocks)
        {
            _reason = $"Output stopped after {MaxBlocks:N0} blocks from this cell. The cell itself kept running, and only its output was capped.";
            return false;
        }

        if ((long)_characters + content.Length > MaxCharacters)
        {
            _reason = DescribeCharacterLimit();
            return false;
        }

        _characters += content.Length;
        return true;
    }

    /// <summary>
    /// Accounts for a block being rewritten in place, which is how streamed text and progress
    /// reports grow. Returns the text to store, shortened when the replacement would carry the cell
    /// past its character limit, or null when the block may not be revised any further.
    /// </summary>
    /// <param name="previousContent">What the block holds now.</param>
    /// <param name="content">What the kernel wants it to hold.</param>
    public string? TryReviseBlock(string previousContent, string content)
    {
        if (_reason is not null)
            return null;

        var others = _characters - previousContent.Length;
        if ((long)others + content.Length <= MaxCharacters)
        {
            _characters = others + content.Length;
            return content;
        }

        _reason = DescribeCharacterLimit();

        var allowance = MaxCharacters - others;
        if (allowance <= 0)
            return null;

        // Never split a surrogate pair, which would leave an unpaired code unit that serializers
        // and browsers both render as a replacement character.
        if (char.IsHighSurrogate(content[allowance - 1]))
            allowance--;

        _characters = others + allowance;
        return allowance > 0 ? content[..allowance] : null;
    }

    /// <summary>
    /// Yields the notice explaining the limit, once, the first time it is asked after the limit is
    /// reached. Returns null at every other moment, so a caller can ask on every rejected output
    /// without the notice repeating.
    /// </summary>
    public CellOutput? TryDescribeLimit()
    {
        if (_reason is null || _reported)
            return null;

        _reported = true;
        return CellOutput.Plain(_reason);
    }

    private static string DescribeCharacterLimit() =>
        $"Output stopped at {MaxCharacters / (1024 * 1024)} MB from this cell. The cell itself kept running, and only its output was capped.";
}
