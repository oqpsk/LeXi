using System.Net;
using Lexi.Core;

// The sentence a word was selected in picks one of the entry's senses for the TL;DR.
internal static class ContextChecks
{
    public static async Task Run(string fixture, string root)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }

        const string text = "Prices rose. We sat on the bank of the river until dark! Then we left.";
        var start = text.IndexOf("bank", StringComparison.Ordinal);
        Check(LookupContext.Sentence(text, start, 4) == "We sat on the bank of the river until dark!",
            "the sentence around a selection stops at the neighbouring sentence ends");
        const string abbreviation = "The U.S. economy grew.";
        Check(LookupContext.Sentence(abbreviation, abbreviation.IndexOf("U.S.", StringComparison.Ordinal), 4) == "The U.S. economy grew.",
            "a full stop inside the selection does not cut the sentence");
        Check(LookupContext.Sentence("bank", 0, 4) == null && LookupContext.Sentence(null, 0, 4) == null &&
            LookupContext.Sentence(text, -1, 4) == null && LookupContext.Sentence(text, text.Length - 2, 4) == null,
            "no sentence beyond the selection, or offsets outside the text, give no context");
        var long_ = string.Join(" ", Enumerable.Repeat("word", 300)) + " bank " + string.Join(" ", Enumerable.Repeat("word", 300));
        var around = LookupContext.Sentence(long_, long_.IndexOf("bank", StringComparison.Ordinal), 4);
        Check(around != null && around.Length <= LookupContext.SentenceLimit && around.Contains("bank"),
            "a very long sentence is cut to a window around the selection");
        Check(LookupCandidates.From("bank", text, start) is { Submit: true, Context: "We sat on the bank of the river until dark!" } &&
            LookupCandidates.From("bank") is { Context: null } && LookupCandidates.From(text, text, 0) is { Context: null },
            "a selected word carries its sentence; copied text and long selections do not");

        var senses = new[] { new ContextSense("money", "名词", "银行", "a financial institution"), new ContextSense("river", "名词", "河岸", "the land beside a river") };
        var request = new ContextRequest("bank", "We sat on the bank of the river.", senses);
        string Json(string id, string gloss, string? hash = null) =>
            System.Text.Json.JsonSerializer.Serialize(new ContextPick(hash ?? request.InputHash, id, gloss), Lexi.Core.Json.Options);
        Check(LookupContext.Parse(Json("river", " 河岸 "), request) == new ContextPick(request.InputHash, "river", "河岸") &&
            LookupContext.Parse(Json("", "岸边"), request).SenseId == "" && LookupContext.Parse(Json("", ""), request).Gloss == "",
            "a pick names a supplied sense or none, with a short Chinese gloss");
        Check(new[] { Json("river", "河岸", "other"), Json("nowhere", "河岸"), Json("river", ""), Json("river", new string('岸', 41)) }
            .All(json => { try { LookupContext.Parse(json, request); return false; } catch (InvalidDataException) { return true; } }),
            "a pick for another request, an unknown sense, a sense without gloss or an overlong gloss is rejected");

        var work = Path.Combine(Path.GetFullPath(root), "context-" + Guid.NewGuid().ToString("N"));
        int picks = 0;
        var model = new LookupModel("codex", "synthetic-context");
        var entry = new AiEntry("bank", true, [
            new("noun", "a financial institution", "银行", "", "", "", [], Gloss: "银行", CoreMeaning: true, ChineseTerm: "银行"),
            new("noun", "the land beside a river", "河岸", "", "", "", [], Gloss: "河岸", ChineseTerm: "河岸")], "", "unverified");
        var providers = new QueryProviders((_, _, _, _, _, _) => Task.FromResult(entry),
            (_, _, _) => throw new Exception("No judge in this check"),
            (_, _, _, _, _) => throw new Exception("No translation in this check"),
            (asked, _, _) =>
            {
                picks++;
                var river = asked.Senses.Single(s => s.Chinese == "河岸");
                return Task.FromResult(new ContextPick(asked.InputHash, river.Id, "河岸（河边的地）"));
            });
        using var network = new NotFound();
        using var session = new QuerySession(new(work, Path.GetFullPath(fixture), Path.Combine(work, "user")), providers: providers, network: network);
        session.Settings.Provider = model.Provider; session.Settings.Model = model.Model; session.Settings.ParallelLookup = false; session.Settings.MergeSenses = false;
        await session.InitializeAsync();
        await session.SubmitAsync("bank", context: "We sat on the bank of the river.");
        var view = session.View;
        var marked = view.Answers?.Senses.Where(s => s.Heading.InContext).ToArray() ?? [];
        Check(picks == 1 && view.QuickRead is { Text: "河岸（河边的地）", Caption: "TL;DR · 本句语境", ShowState: true } &&
            view.QuickRead.Tooltip.Contains("We sat on the bank of the river.") && marked.Length == 1 && marked[0].Chinese == "河岸" &&
            view.Answers!.Senses.Length == 2,
            "a sentence turns the TL;DR into its meaning there and marks that sense, keeping every sense");
        await session.SubmitAsync("bank", context: "We sat on the bank of the river.");
        Check(picks == 1 && session.View.QuickRead.Text == "河岸（河边的地）", "the same word in the same sentence reuses the saved pick");
        await session.RegenerateAsync();
        Check(session.View.QuickRead.Caption == "TL;DR · 本句语境", "regenerating the page keeps its sentence");
        await session.SubmitAsync("bank");
        Check(session.View.QuickRead.Caption != "TL;DR · 本句语境" && session.View.Answers!.Senses.All(s => !s.Heading.InContext),
            "the same word without a sentence shows its general overview");
        session.Settings.Online = false;
        var before = picks;
        await session.SubmitAsync("bank", context: "He works at the bank downtown.");
        Check(picks == before && session.View.QuickRead.Caption != "TL;DR · 本句语境", "offline lookups never ask a model about the sentence");
        Directory.Delete(work, true);
    }

    private sealed class NotFound : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
