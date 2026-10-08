namespace InstaPrompt.Core.Search;

/// <summary>Small subsequence matcher with bonuses for prefixes, word starts and consecutive characters.</summary>
public static class FuzzyMatcher
{
    /// <summary>Returns a score (higher is better) or null if <paramref name="query"/> does not match.</summary>
    public static int? Score(string query, string text)
    {
        if (query.Length == 0) return 0;
        if (text.Length == 0) return null;

        // An exact substring always beats a scattered subsequence.
        var at = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (at >= 0)
        {
            // No length penalty: equally good matches keep the user's own prompt order.
            var score = 1000 - at * 2;
            if (at == 0) score += 200;
            else if (IsWordStart(text, at)) score += 100;
            return score;
        }

        var total = 0;
        var last = -1;
        var position = 0;
        foreach (var ch in query)
        {
            var index = text.IndexOf(ch.ToString(), position, StringComparison.CurrentCultureIgnoreCase);
            if (index < 0) return null;

            total += 10;
            if (index == last + 1) total += 15;
            if (IsWordStart(text, index)) total += 20;
            if (last >= 0) total -= Math.Min(index - last - 1, 10);
            last = index;
            position = index + 1;
        }

        return total;
    }

    /// <summary>Filters and ranks items; ties keep their original order. An empty query returns everything unchanged.</summary>
    public static List<T> Filter<T>(IEnumerable<T> items, string query, Func<T, string> text)
    {
        query = query.Trim();
        if (query.Length == 0) return items.ToList();

        return items
            .Select((item, order) => (item, order, score: Score(query, text(item))))
            .Where(x => x.score.HasValue)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.order)
            .Select(x => x.item)
            .ToList();
    }

    private static bool IsWordStart(string text, int index)
        => index == 0 || !char.IsLetterOrDigit(text[index - 1]);
}
