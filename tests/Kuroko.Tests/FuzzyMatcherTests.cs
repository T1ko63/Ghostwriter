using Kuroko.Core.Search;

namespace Kuroko.Tests;

public class FuzzyMatcherTests
{
    private static readonly string[] Names =
        ["Correction", "Rewrite", "Condense", "Expand", "Professioneller", "Lockerer", "Übersetzen DE↔EN", "Auftrag"];

    [Fact]
    public void Empty_query_returns_everything_in_original_order()
    {
        Assert.Equal(Names, FuzzyMatcher.Filter(Names, "  ", n => n));
    }

    [Fact]
    public void Non_matching_query_returns_nothing()
    {
        Assert.Empty(FuzzyMatcher.Filter(Names, "xyz", n => n));
    }

    [Fact]
    public void Match_is_case_insensitive()
    {
        Assert.Contains("Correction", FuzzyMatcher.Filter(Names, "CORR", n => n));
    }

    [Fact]
    public void Prefix_beats_scattered_subsequence()
    {
        // "co" is a prefix of Correction and Condense; scattered in e.g. "Professioneller" would rank lower.
        var result = FuzzyMatcher.Filter(Names, "co", n => n);
        Assert.Equal("Correction", result[0]);
        Assert.Equal("Condense", result[1]);
    }

    [Fact]
    public void Subsequence_matches_across_gaps()
    {
        Assert.Contains("Professioneller", FuzzyMatcher.Filter(Names, "pfl", n => n));
    }

    [Fact]
    public void Word_start_beats_middle_of_word()
    {
        var items = new[] { "Dateien umbenennen", "Fundament" };
        Assert.Equal("Dateien umbenennen", FuzzyMatcher.Filter(items, "um", n => n)[0]);
    }

    [Fact]
    public void Query_longer_than_text_does_not_match()
    {
        Assert.Null(FuzzyMatcher.Score("correctionx", "Correction"));
    }

    [Fact]
    public void Umlauts_match()
    {
        Assert.Contains("Übersetzen DE↔EN", FuzzyMatcher.Filter(Names, "übers", n => n));
    }
}
