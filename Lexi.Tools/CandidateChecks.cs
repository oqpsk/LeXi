using Lexi.Core;

// The text taken from another app's selection or a fresh clipboard when LeXi is summoned.
internal static class CandidateChecks
{
    public static void Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        Check(LookupCandidates.From("serendipity,") == new LookupCandidate("serendipity", true) &&
            LookupCandidates.From("“ephemeral”") == new LookupCandidate("ephemeral", true) &&
            LookupCandidates.From("  《红楼梦》 ") == new LookupCandidate("红楼梦", true) &&
            LookupCandidates.From("word.") == new LookupCandidate("word", true),
            "a copied word loses the quotes and punctuation that came with it");
        Check(LookupCandidates.From("U.S.") == new LookupCandidate("U.S.", true) &&
            LookupCandidates.From("e.g.") == new LookupCandidate("e.g.", true) &&
            LookupCandidates.From("The ship is ready.") == new LookupCandidate("The ship is ready.", true),
            "abbreviations and short sentences keep their final full stop");
        var paragraph = string.Join(" ", Enumerable.Repeat("The ship is ready to leave the harbour.", 4));
        Check(LookupCandidates.From(paragraph) is { Submit: false } && LookupCandidates.From("first line\nsecond line") is { Submit: false },
            "longer or multi-line text only fills the query field");
        Check(new[] { "https://example.org/a", "www.example.org", "someone@example.org", "/Users/someone/file.txt", "~/notes", "C:\\Temp\\a.txt", "", "   ", "a\u0001b", new string('x', 5001) }
            .All(t => LookupCandidates.From(t) == null), "links, paths, addresses, blanks, control characters and oversized text are not lookups");
    }
}
