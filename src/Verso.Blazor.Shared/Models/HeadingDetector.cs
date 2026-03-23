using Verso.Abstractions;

namespace Verso.Blazor.Shared.Models;

public static class HeadingDetector
{
    public static int? GetHeadingLevel(CellModel cell)
    {
        if (string.IsNullOrWhiteSpace(cell.Source))
            return null;

        if (string.Equals(cell.Type, "markdown", StringComparison.OrdinalIgnoreCase))
            return GetMarkdownHeadingLevel(cell.Source);

        if (string.Equals(cell.Type, "html", StringComparison.OrdinalIgnoreCase))
            return GetHtmlHeadingLevel(cell.Source);

        return null;
    }

    private static int? GetMarkdownHeadingLevel(string source)
    {
        int level = 0;
        while (level < source.Length && source[level] == '#' && level < 6)
            level++;

        if (level == 0)
            return null;

        if (level >= source.Length)
            return null;

        if (source[level] != ' ' && source[level] != '\t')
            return null;

        return level;
    }

    private static int? GetHtmlHeadingLevel(string source)
    {
        var trimmed = source.TrimStart();
        if (trimmed.Length < 4 || trimmed[0] != '<')
            return null;

        if (trimmed[1] != 'h' && trimmed[1] != 'H')
            return null;

        var ch = trimmed[2];
        if (ch < '1' || ch > '6')
            return null;

        if (trimmed.Length > 3 && trimmed[3] != '>' && trimmed[3] != ' '
            && trimmed[3] != '\t' && trimmed[3] != '\r' && trimmed[3] != '\n')
            return null;

        return ch - '0';
    }
}
