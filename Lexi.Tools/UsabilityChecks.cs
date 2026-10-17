using Lexi.Core;

internal static class UsabilityChecks
{
    public static void Run(string folder)
    {
        int count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); count++; }
        var parsed = WordForms.Parse("p:learned,learnt/d:learned,learnt/i:learning/3:learns/broken");
        Check(parsed["p"].SequenceEqual(["learned", "learnt"]) && !parsed.ContainsKey("broken"), "word forms retain alternate spellings and ignore malformed fields");
        using var library = new OfflineLibrary(folder);
        var settings = new AppSettings { Online = false };
        var regular = library.Forms("walk", settings).Single();
        Check(regular.Forms.Single(f => f.Kind == "p").Words.Contains("walked") && regular.Forms.Single(f => f.Kind == "d").Words.Contains("walked") &&
            regular.Forms.Single(f => f.Kind == "i").Words.Contains("walking") && regular.Forms.Single(f => f.Kind == "3").Words.Contains("walks"), "regular verb exposes all four recorded verb forms");
        var irregular = library.Forms("went", settings).Single();
        Check(irregular.Lemma == "go" && irregular.Roles("went").SequenceEqual(["过去式"]) && irregular.Forms.Single(f => f.Kind == "d").Words.Contains("gone"),
            "an exact inflected entry without exchange data still resolves through the lemma index");
        Check(library.Forms("walked", settings).Single().Roles("walked").SequenceEqual(["过去式", "过去分词"]), "one spelling retains both past-tense and participle roles");
        Check(library.Forms("serendipity", settings).Length == 0 && library.Forms("not-a-recorded-verb", settings).Length == 0, "missing inflection data does not invent suffix rules");
        settings.DisabledDictionaries = ["ecdict"];
        Check(library.Forms("went", settings).Length == 0, "disabled source cannot supply word forms");
        settings.DisabledDictionaries = []; settings.OfflineEnabled = false;
        Check(library.Forms("went", settings).Length == 0, "offline master switch applies to word forms");
        Check(LexicalLabels.PartOfSpeech("verb (gerund/present participle)") == "动词" && LexicalLabels.Form("verb (gerund/present participle)") == "动名词 / 现在分词", "inflection description is separate from the prominent verb label");
        Check(LexicalLabels.PartOfSpeech("noun") == "名词" && LexicalLabels.PartOfSpeech("adverb") == "副词" && LexicalLabels.PartOfSpeech("pronoun") == "代词", "word-class labels do not confuse embedded noun and verb strings");
        Check(LexicalLabels.Usage("formal") == "书面语" && LexicalLabels.Usage("technical, computing") == "计算机" &&
            LexicalLabels.Usage("standard") == "" && LexicalLabels.Usage("slang, derogatory") == "俚语 · 贬义", "usage labels favor readable Chinese and omit neutral boilerplate");
        Check(LexicalLabels.Usage("计算机；信息安全") == "计算机 · 信息安全" && LexicalLabels.PartOfSpeech("unknown") == "词语", "unknown labels are not falsely assigned a word class");
        Check(ProviderConnection.Parse("claude", 0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\",\"email\":\"someone@example.org\"}") is { SignedIn: true, Message: "已登录 Claude 订阅" }, "Claude status exposes mode without account identifiers");
        Check(!ProviderConnection.Parse("claude", 1, "{\"loggedIn\":false}").SignedIn && !ProviderConnection.Parse("claude", 0, "unexpected output").SignedIn &&
            !ProviderConnection.Parse("claude", 0, "[]").SignedIn, "logged-out and unsupported status responses stay distinct from signed in");
        Check(ProviderConnection.Parse("codex", 0, "Logged in using ChatGPT").Message == "已登录 ChatGPT 订阅" &&
            !ProviderConnection.Parse("codex", 1, "account-specific diagnostic").Message.Contains("diagnostic"), "Codex status never exposes raw diagnostics");
        var exe = Environment.ProcessPath!;
        var login = ProviderConnection.Command("codex", exe, true)!;
        var status = ProviderConnection.Command("codex", exe, false)!;
        Check(login.ArgumentList.SequenceEqual(["login"]) && !login.CreateNoWindow && !login.RedirectStandardOutput &&
            status.ArgumentList.SequenceEqual(["login", "status"]) && status.CreateNoWindow && status.RedirectStandardOutput,
            "login is explicitly interactive while status is hidden and neither command invokes a shell");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { ProviderConnection.CheckAsync("codex", exe, cancel.Token).GetAwaiter().GetResult(); Check(false, "canceled status probe never starts a client"); }
        catch (OperationCanceledException) { Check(true, "canceled status probe never starts a client"); }
        Check(ProviderConnection.Command("codex", Path.Combine(folder, "missing-client.exe"), false) == null, "missing configured client provides a recoverable connection state");
        Console.WriteLine($"{count} dictionary usability and connection checks passed");
    }
}
