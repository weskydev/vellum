namespace Vellum.Services;

public enum DiffKind : byte
{
    Same,
    Removed,
    Added,
}

public readonly record struct DiffLine(DiffKind Kind, string Text);

/// <summary>Line-by-line comparison of two texts, for "what changed" in the version history.</summary>
public static class LineDiff
{
    // Above this many line pairs the middle part is shown as "all removed, all added" instead of matched
    private const long MaxCells = 4_000_000;

    public static List<DiffLine> Compare(string oldText, string newText)
    {
        string[] a = Split(oldText);
        string[] b = Split(newText);

        int start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start]) start++;
        int endA = a.Length, endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
        }

        var result = new List<DiffLine>(Math.Max(a.Length, b.Length) + 8);
        for (int i = 0; i < start; i++) result.Add(new DiffLine(DiffKind.Same, a[i]));

        int n = endA - start, m = endB - start;
        if ((long)n * m <= MaxCells)
        {
            Middle(a, start, n, b, start, m, result);
        }
        else
        {
            for (int i = 0; i < n; i++) result.Add(new DiffLine(DiffKind.Removed, a[start + i]));
            for (int j = 0; j < m; j++) result.Add(new DiffLine(DiffKind.Added, b[start + j]));
        }

        for (int i = endA; i < a.Length; i++) result.Add(new DiffLine(DiffKind.Same, a[i]));
        return result;
    }

    /// <summary>Longest common subsequence of the lines that differ.</summary>
    private static void Middle(string[] a, int offA, int n, string[] b, int offB, int m, List<DiffLine> result)
    {
        var lengths = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                lengths[i, j] = a[offA + i] == b[offB + j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[offA + x] == b[offB + y])
            {
                result.Add(new DiffLine(DiffKind.Same, a[offA + x]));
                x++;
                y++;
            }
            else if (lengths[x + 1, y] >= lengths[x, y + 1])
            {
                result.Add(new DiffLine(DiffKind.Removed, a[offA + x++]));
            }
            else
            {
                result.Add(new DiffLine(DiffKind.Added, b[offB + y++]));
            }
        }
        while (x < n) result.Add(new DiffLine(DiffKind.Removed, a[offA + x++]));
        while (y < m) result.Add(new DiffLine(DiffKind.Added, b[offB + y++]));
    }

    private static string[] Split(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
