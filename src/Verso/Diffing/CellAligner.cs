using System.Text.Json;
using Verso.Abstractions;

namespace Verso.Diffing;

/// <summary>
/// Aligns the cells of two notebook versions into an ordered list of <see cref="CellDiffEntry"/> items.
/// Cells are matched by id first, then by exact content, and finally by position within the gaps
/// between matched neighbors.
/// </summary>
internal static class CellAligner
{
    /// <summary>
    /// Aligns two cell lists into diff entries. <paramref name="transientOutputTypes"/> names the
    /// cell types whose outputs are derived from the source rather than authored, and so take no
    /// part in the comparison; when it is null or empty, all outputs are compared.
    /// </summary>
    public static List<CellDiffEntry> Align(
        IReadOnlyList<CellModel> baseline,
        IReadOnlyList<CellModel> current,
        IReadOnlySet<string>? transientOutputTypes = null)
    {
        var baselineConsumed = new bool[baseline.Count];
        var currentConsumed = new bool[current.Count];

        // Pass 1: pair cells whose ids match.
        var idPairs = new List<(int BaselineIndex, int CurrentIndex)>();
        var baselineIdIndex = new Dictionary<Guid, int>();
        for (var i = 0; i < baseline.Count; i++)
        {
            baselineIdIndex.TryAdd(baseline[i].Id, i);
        }

        for (var j = 0; j < current.Count; j++)
        {
            if (baselineIdIndex.TryGetValue(current[j].Id, out var i) && !baselineConsumed[i])
            {
                idPairs.Add((i, j));
                baselineConsumed[i] = true;
                currentConsumed[j] = true;
            }
        }

        // Pass 2: id pairs whose relative order survived form the anchor chain; the rest moved.
        idPairs.Sort((a, b) => a.BaselineIndex.CompareTo(b.BaselineIndex));
        var inChain = LongestIncreasingSubsequence(idPairs.Select(p => p.CurrentIndex).ToList());

        // Pass 3: exact-content matching over the cells that never matched by id.
        var leftoverBaseline = Enumerable.Range(0, baseline.Count).Where(i => !baselineConsumed[i]).ToList();
        var leftoverCurrent = Enumerable.Range(0, current.Count).Where(j => !currentConsumed[j]).ToList();
        var contentPairs = LongestCommonSubsequence(
            leftoverBaseline, leftoverCurrent,
            (i, j) => ContentEquals(baseline[i], current[j]));
        foreach (var (i, j) in contentPairs)
        {
            baselineConsumed[i] = true;
            currentConsumed[j] = true;
        }

        // Pass 4: zip the remaining cells positionally within the gaps between anchors.
        // Id-chain and content pairs can cross each other, so keep only a mutually
        // order-consistent subset as gap boundaries.
        var anchors = idPairs.Where((_, k) => inChain[k]).Concat(contentPairs).ToList();
        anchors.Sort((a, b) => a.CurrentIndex.CompareTo(b.CurrentIndex));
        var consistent = LongestIncreasingSubsequence(anchors.Select(p => p.BaselineIndex).ToList());
        anchors = anchors.Where((_, k) => consistent[k]).ToList();
        var gapPairs = PairWithinGaps(
            anchors,
            Enumerable.Range(0, baseline.Count).Where(i => !baselineConsumed[i]).ToList(),
            Enumerable.Range(0, current.Count).Where(j => !currentConsumed[j]).ToList(),
            (i, j) => AreSimilar(baseline[i], current[j]));
        foreach (var (i, j) in gapPairs)
        {
            baselineConsumed[i] = true;
            currentConsumed[j] = true;
        }

        // Build entries for everything matched to a current-side position.
        var entryByCurrent = new CellDiffEntry?[current.Count];
        var currentByBaseline = new Dictionary<int, int>();

        for (var k = 0; k < idPairs.Count; k++)
        {
            var (i, j) = idPairs[k];
            var entry = CreatePairEntry(baseline[i], current[j], i, j, matchedByContent: false, transientOutputTypes);
            if (entry.Kind == CellDiffKind.Unchanged && !inChain[k])
            {
                entry.Kind = CellDiffKind.Moved;
            }

            entryByCurrent[j] = entry;
            currentByBaseline[i] = j;
        }

        foreach (var (i, j) in contentPairs.Concat(gapPairs))
        {
            entryByCurrent[j] = CreatePairEntry(baseline[i], current[j], i, j, matchedByContent: true, transientOutputTypes);
            currentByBaseline[i] = j;
        }

        for (var j = 0; j < current.Count; j++)
        {
            entryByCurrent[j] ??= new CellDiffEntry
            {
                Kind = CellDiffKind.Added,
                CurrentIndex = j,
                CurrentCell = current[j],
            };
        }

        // Splice each removed cell in after the nearest matched baseline predecessor.
        var removedAfter = new Dictionary<int, List<CellDiffEntry>>();
        for (var i = 0; i < baseline.Count; i++)
        {
            if (baselineConsumed[i])
            {
                continue;
            }

            var anchorCurrent = -1;
            for (var p = i - 1; p >= 0; p--)
            {
                if (currentByBaseline.TryGetValue(p, out var j))
                {
                    anchorCurrent = j;
                    break;
                }
            }

            if (!removedAfter.TryGetValue(anchorCurrent, out var list))
            {
                removedAfter[anchorCurrent] = list = new List<CellDiffEntry>();
            }

            list.Add(new CellDiffEntry
            {
                Kind = CellDiffKind.Removed,
                BaselineIndex = i,
                BaselineCell = baseline[i],
            });
        }

        var entries = new List<CellDiffEntry>(baseline.Count + current.Count);
        if (removedAfter.TryGetValue(-1, out var leading))
        {
            entries.AddRange(leading);
        }

        for (var j = 0; j < current.Count; j++)
        {
            entries.Add(entryByCurrent[j]!);
            if (removedAfter.TryGetValue(j, out var trailing))
            {
                entries.AddRange(trailing);
            }
        }

        return entries;
    }

    private static CellDiffEntry CreatePairEntry(
        CellModel baselineCell,
        CellModel currentCell,
        int baselineIndex,
        int currentIndex,
        bool matchedByContent,
        IReadOnlySet<string>? transientOutputTypes)
    {
        var entry = new CellDiffEntry
        {
            BaselineIndex = baselineIndex,
            CurrentIndex = currentIndex,
            BaselineCell = baselineCell,
            CurrentCell = currentCell,
            MatchedByContent = matchedByContent,
            SourceChanged = !string.Equals(baselineCell.Source, currentCell.Source, StringComparison.Ordinal),
            OutputsChanged = ComparesOutputs(baselineCell, currentCell, transientOutputTypes)
                && !baselineCell.Outputs.SequenceEqual(currentCell.Outputs),
            TypeOrLanguageChanged =
                !string.Equals(baselineCell.Type, currentCell.Type, StringComparison.Ordinal)
                || !string.Equals(baselineCell.Language, currentCell.Language, StringComparison.Ordinal),
            CellMetadataChanged = !MetadataEquals(baselineCell.Metadata, currentCell.Metadata),
        };

        entry.Kind = entry.SourceChanged || entry.OutputsChanged || entry.TypeOrLanguageChanged || entry.CellMetadataChanged
            ? CellDiffKind.Modified
            : CellDiffKind.Unchanged;
        return entry;
    }

    /// <summary>
    /// Whether the two sides' outputs are worth comparing. They are not when both cells are of a
    /// type whose outputs are derived from the source: those are re-rendered on open and never
    /// saved, so one side having them and the other not says nothing about what was edited.
    /// Both sides must qualify, so converting a markup cell into a kernel cell still reports the
    /// real outputs the new cell gained.
    /// </summary>
    private static bool ComparesOutputs(CellModel a, CellModel b, IReadOnlySet<string>? transientOutputTypes)
        => transientOutputTypes is not { Count: > 0 }
            || !(transientOutputTypes.Contains(a.Type) && transientOutputTypes.Contains(b.Type));

    private static bool ContentEquals(CellModel a, CellModel b)
        => string.Equals(a.Type, b.Type, StringComparison.Ordinal)
            && string.Equals(a.Language, b.Language, StringComparison.Ordinal)
            && string.Equals(a.Source, b.Source, StringComparison.Ordinal);

    private static bool MetadataEquals(Dictionary<string, object> a, Dictionary<string, object> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, valueA) in a)
        {
            if (!b.TryGetValue(key, out var valueB))
            {
                return false;
            }

            if (!string.Equals(StringifyMetadataValue(valueA), StringifyMetadataValue(valueB), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Renders a metadata value as JSON for comparison, so nested dictionaries and lists compare
    /// structurally instead of by reference.
    /// </summary>
    private static string StringifyMetadataValue(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        try
        {
            return JsonSerializer.Serialize(value);
        }
        catch (NotSupportedException)
        {
            return value.ToString() ?? "";
        }
    }

    /// <summary>
    /// Marks which elements of <paramref name="values"/> participate in a longest strictly
    /// increasing subsequence.
    /// </summary>
    private static bool[] LongestIncreasingSubsequence(IReadOnlyList<int> values)
    {
        var result = new bool[values.Count];
        if (values.Count == 0)
        {
            return result;
        }

        // tails[l] holds the index into values of the smallest tail of any increasing
        // subsequence of length l + 1; predecessor links reconstruct the chosen chain.
        var tails = new List<int>();
        var previous = new int[values.Count];
        for (var k = 0; k < values.Count; k++)
        {
            var low = 0;
            var high = tails.Count;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (values[tails[mid]] < values[k])
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            previous[k] = low > 0 ? tails[low - 1] : -1;
            if (low == tails.Count)
            {
                tails.Add(k);
            }
            else
            {
                tails[low] = k;
            }
        }

        for (var k = tails[^1]; k >= 0; k = previous[k])
        {
            result[k] = true;
        }

        return result;
    }

    /// <summary>
    /// Classic dynamic-programming longest common subsequence over two index lists with a custom
    /// equality predicate. Returns matched (baselineIndex, currentIndex) pairs in ascending order.
    /// </summary>
    private static List<(int BaselineIndex, int CurrentIndex)> LongestCommonSubsequence(
        IReadOnlyList<int> baselineIndices,
        IReadOnlyList<int> currentIndices,
        Func<int, int, bool> equals)
    {
        var m = baselineIndices.Count;
        var n = currentIndices.Count;
        var pairs = new List<(int, int)>();
        if (m == 0 || n == 0)
        {
            return pairs;
        }

        var lengths = new int[m + 1, n + 1];
        for (var a = m - 1; a >= 0; a--)
        {
            for (var b = n - 1; b >= 0; b--)
            {
                lengths[a, b] = equals(baselineIndices[a], currentIndices[b])
                    ? lengths[a + 1, b + 1] + 1
                    : Math.Max(lengths[a + 1, b], lengths[a, b + 1]);
            }
        }

        var x = 0;
        var y = 0;
        while (x < m && y < n)
        {
            if (equals(baselineIndices[x], currentIndices[y]))
            {
                pairs.Add((baselineIndices[x], currentIndices[y]));
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }

    /// <summary>
    /// Pairs leftover baseline and current cells that fall between the same pair of anchors,
    /// preserving relative order and pairing only cells the similarity gate accepts, so an
    /// unrelated removal plus addition is not misreported as a modification. Anchors must be
    /// sorted by current index.
    /// </summary>
    private static List<(int BaselineIndex, int CurrentIndex)> PairWithinGaps(
        IReadOnlyList<(int BaselineIndex, int CurrentIndex)> anchors,
        IReadOnlyList<int> leftoverBaseline,
        IReadOnlyList<int> leftoverCurrent,
        Func<int, int, bool> similar)
    {
        var pairs = new List<(int, int)>();
        var gapCount = anchors.Count + 1;
        for (var gap = 0; gap < gapCount; gap++)
        {
            var baselineLow = gap > 0 ? anchors[gap - 1].BaselineIndex : -1;
            var baselineHigh = gap < anchors.Count ? anchors[gap].BaselineIndex : int.MaxValue;
            var currentLow = gap > 0 ? anchors[gap - 1].CurrentIndex : -1;
            var currentHigh = gap < anchors.Count ? anchors[gap].CurrentIndex : int.MaxValue;

            var gapBaseline = leftoverBaseline.Where(i => i > baselineLow && i < baselineHigh).ToList();
            var gapCurrent = leftoverCurrent.Where(j => j > currentLow && j < currentHigh).ToList();
            pairs.AddRange(LongestCommonSubsequence(gapBaseline, gapCurrent, similar));
        }

        return pairs;
    }

    private const double SimilarityThreshold = 0.4;

    /// <summary>
    /// Loose similarity gate for positional pairing: same cell type and enough shared source
    /// tokens (Dice coefficient over the token sets) that the two sides plausibly represent an
    /// edit of one cell rather than unrelated content.
    /// </summary>
    private static bool AreSimilar(CellModel a, CellModel b)
    {
        if (!string.Equals(a.Type, b.Type, StringComparison.Ordinal))
        {
            return false;
        }

        var tokensA = Tokenize(a.Source);
        var tokensB = Tokenize(b.Source);
        if (tokensA.Count == 0 && tokensB.Count == 0)
        {
            return true;
        }

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return false;
        }

        var intersection = tokensA.Count(tokensB.Contains);
        var dice = 2.0 * intersection / (tokensA.Count + tokensB.Count);
        return dice >= SimilarityThreshold;
    }

    private static HashSet<string> Tokenize(string source)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var k = 0; k < source.Length; k++)
        {
            if (char.IsLetterOrDigit(source[k]) || source[k] == '_')
            {
                if (start < 0)
                {
                    start = k;
                }
            }
            else if (start >= 0)
            {
                tokens.Add(source[start..k]);
                start = -1;
            }
        }

        if (start >= 0)
        {
            tokens.Add(source[start..]);
        }

        return tokens;
    }
}
