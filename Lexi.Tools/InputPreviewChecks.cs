using Lexi.Core;

internal static class InputPreviewChecks
{
    public static async Task Run(string fixture, string root)
    {
        void Check(bool ok,string name) { if (!ok) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); }
        var work = Path.Combine(Path.GetFullPath(root),"input-preview-"+Guid.NewGuid().ToString("N"));
        int providersCalled=0, networkCalled=0, views=0;
        var providers = new QueryProviders((_,_,_,_,_,_) => { providersCalled++; throw new Exception("Preview must not call a model"); },
            (_,_,_) => { providersCalled++; throw new Exception("Preview must not judge"); },
            (_,_,_,_,_) => { providersCalled++; throw new Exception("Preview must not translate"); });
        using var network = new RejectNetwork(() => networkCalled++);
        using var session = new QuerySession(new(work,Path.GetFullPath(fixture),Path.Combine(work,"user")),providers:providers,network:network);
        session.Changed += _ => views++;
        var original = session.View;
        var preview = await session.PreviewAsync("Hello");
        Check(preview is { Query:"Hello", Headword:"hello", Text:"你好" } && preview.Source.Contains("词典摘录"),
            "input preview reads the exact local entry and reports the real case-sensitive headword");
        Check(await session.PreviewAsync("hel") == null && session.Suggest("hel").Contains("hello"),
            "a matching prefix can suggest a word but cannot invent an exact-word preview");
        preview = await session.PreviewAsync("walked");
        Check(preview is { Query:"walked", Headword:"walk" },"an indexed inflection explicitly identifies its matched lemma");

        var model = new LookupModel("codex","synthetic-saved");
        var entry = new AiEntry("HELLO",true,[new("noun","A synthetic abbreviation.","已保存的缩写含义","", "", "", [],Gloss:"已保存的缩写含义",CoreMeaning:true)],"","unverified");
        var answers = new[] {new ModelAnswer(model,ModelPhase.Complete,entry)};
        var saved = new SavedDictionary("HELLO",[],[],[],[],null,null,new(answers,SenseMerging.Group(answers)),QueryPresentation.NoPronunciation,true,false,DateTimeOffset.UtcNow.AddYears(-1));
        await session.Saved.SaveDictionaryAsync(saved,CancellationToken.None);
        var savedPath = Directory.GetFiles(Path.Combine(work,"user"),"*",SearchOption.AllDirectories).ToDictionary(p=>p,File.GetLastWriteTimeUtc);
        preview = await session.PreviewAsync("  HELLO  ");
        Check(preview is { Query:"HELLO", Headword:"HELLO", Text:"已保存的缩写含义" } && preview.Source.StartsWith("已保存结果"),
            "a completed saved TLDR takes priority over local text and keeps abbreviation identity");
        Check((await session.PreviewAsync("hello"))?.Text == "你好","saved uppercase meanings never leak into a lowercase homograph");
        session.Settings.OfflineEnabled=false;
        Check((await session.PreviewAsync("HELLO"))?.Text == "已保存的缩写含义" && await session.PreviewAsync("hello") == null,
            "saved results remain available with offline dictionaries disabled, while local excerpts respect the switch");
        session.Settings.OfflineEnabled=true;
        session.Settings.DisabledDictionaries=(await session.Library).Dictionaries.Select(d=>d.Id).ToArray();
        Check(await session.PreviewAsync("hello") == null,"input preview respects individually disabled dictionaries");
        Check(await session.PreviewAsync("") == null && await session.PreviewAsync("hello\nworld") == null && await session.PreviewAsync(new string('a',101)) == null,
            "empty input and translation-sized text do not create a word preview");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        try { await session.PreviewAsync("hello",stop.Token); Check(false,"cancelled preview"); }
        catch(OperationCanceledException) { Check(true,"a cancelled input preview stops before cache or dictionary work"); }
        Check(ReferenceEquals(original,session.View) && views==0 && session.Current.Length==0 && session.History.Entries.Count==0 &&
            !session.View.Navigation.CanBack && providersCalled==0 && networkCalled==0 &&
            savedPath.All(p=>File.GetLastWriteTimeUtc(p.Key)==p.Value),
            "previews leave the visible query, navigation, history and saved files unchanged and call no network or models");
        var pending = new TaskCompletionSource<OfflineLibrary>();
        using var pendingStop = new CancellationTokenSource();
        var waiting = InputPreview.ReadAsync("hello",session.Saved,pending.Task,new AppSettings(),pendingStop.Token);
        pendingStop.Cancel();
        try { await waiting.WaitAsync(TimeSpan.FromSeconds(5)); Check(false,"cancel pending library"); }
        catch(OperationCanceledException) { Check(true,"preview cancellation also interrupts waiting for the local library"); }
    }
    private sealed class RejectNetwork(Action called) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) { called(); throw new Exception("Preview network access"); } }
}
