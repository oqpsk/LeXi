using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckQuickRead(MainWindow main)
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
        main.ResetSenseDetails(); main.ResetLexicalDetails();
        var brief = SpecialistReferences.Lookup("TEE").Single();
        var evidence = DictionaryEvidence.Combine("TEE", [], null)!;
        var model = new LookupModel("codex", "fixture");
        var sense = new AiSense("noun",brief.English,brief.Chinese,brief.Domain,"","",[1],"abbreviation",brief.Expansion,"可信执行环境");
        var answer = new ModelAnswer(model,ModelPhase.Complete,new AiEntry("TEE",true,[sense],"","source_based"));
        var snapshot = new LookupSnapshot([answer],SenseMerging.Group([answer]));
        var ai = (Panel)main.FindName("AiPanel");
        var summary = (TextBox)main.FindName("QuickReadText");
        var caption = (TextBlock)main.FindName("QuickReadLabel");
        main.BeginLexicalOverview("TEE", []);
        Check(Contents(ai,true).OfType<TextBox>().Count(t=>t.Text==brief.Chinese)==1 && summary.Text.Contains("可信执行环境"),
            "offline specialist lookup has one attributed definition row and an immediate overview");
        Check(!Contents((DependencyObject)main.Content,true).OfType<Expander>().Any(e=>e.Header.ToString()!.Contains("随应用收录")),
            "specialist references no longer occupy a standalone section");
        main.RenderAnswers(snapshot,evidence,false);
        Check(summary.Text=="可信执行环境" && caption.Text.Contains("预览"),
            "completed model gloss is concise but stays provisional until the query is finalized");
        Check(Contents(ai,true).OfType<TextBox>().Count(t=>t.Text==brief.Chinese)==1,
            "a covered specialist definition replaces its fallback instead of appearing twice");
        var streaming = answer with {Phase=ModelPhase.Streaming};
        main.RenderAnswers(new([streaming],SenseMerging.Group([streaming])),evidence,false);
        Check(summary.Text=="可信执行环境" && caption.Text.Contains("预览"), "partial refreshes keep an already completed overview until replacement or failure");
        main.RenderAnswers(snapshot,evidence,false);
        var source = Contents(ai,true).OfType<ToggleButton>().Single(t=>t.Content.ToString()!.StartsWith("参考资料"));
        source.IsChecked=true;
        Check(Contents(ai,true).OfType<TextBox>().Count(t=>t.Text==brief.Chinese)==2,
            "opening the sense references reveals the original brief and its attribution");
        main.RenderAnswers(snapshot,evidence,true);
        Check(caption.Text.Contains("已整理") && Contents(ai,true).OfType<ToggleButton>().Single(t=>t.Content.ToString()!.StartsWith("参考资料")).IsChecked==true,
            "finalized summary and reference disclosure retain their independent states");
        var failed = new ModelAnswer(model,ModelPhase.Failed,null,false,"fixture failure");
        main.RenderAnswers(new([failed],[]),evidence);
        Check(Contents(ai,true).OfType<TextBox>().Any(t=>t.Text==brief.Chinese) && caption.Text.Contains("词典摘录"),
            "provider failure restores the attributed fallback and never leaves a failed model summary");
        main.ResetSenseDetails();
        main.RenderAnswers(new([failed],[]),evidence);
        Contents(ai,true).OfType<ToggleButton>().Single(t=>t.Content.ToString()=="参考资料").IsChecked=true;
        main.RenderAnswers(snapshot,evidence);
        Check(Contents(ai,true).OfType<ToggleButton>().Single(t=>t.Content.ToString()!.StartsWith("参考资料")).IsChecked==true,
            "a fallback reference being read stays open when the matching model sense arrives");
        Check(QueryProgressState.Finished(snapshot,true,false,1).Title.Contains("2 个义项"),
            "final progress totals include visible reference fallback rows");
        main.RenderAnswers(snapshot with { MergeFailed=true },evidence);
        Check(caption.Text.Contains("部分结果"),"a failed merge never marks the overview as fully organized");
        main.RenderAnswers(snapshot,evidence);
        var card = (FrameworkElement)main.FindName("QuickReadCard");
        foreach (var width in new[] {558,718,1078})
        {
            Measure((FrameworkElement)main.Content,width,618);
            Check(card.DesiredSize.Height<=92 && summary.FontSize>=19 && summary.IsReadOnly,
                $"compact selectable overview stays readable at {width+2} DIP");
        }
        var legacy = answer with { Entry=answer.Entry! with {Senses=[sense, sense with {Chinese="另一个概念",Gloss="另一个概念",Expansion="Other concept"}]} };
        main.RenderAnswers(new([legacy],SenseMerging.Group([legacy])),evidence);
        Check(caption.Text.StartsWith("义项摘录"), "unranked cached meanings do not claim to be a curated TLDR");
        var request = SenseMerging.Request("TEE",[legacy]);
        var plan = new SenseMergePlan(request.InputHash,request.Pairs.Select(p=>new SenseVerdict(p.A,p.B,"different",1)).ToArray(),["m0s1"]);
        main.RenderAnswers(new([legacy],SenseMerging.Group([legacy],plan)),evidence);
        Check(caption.Text=="TL;DR · 已整理" && summary.Text=="另一个概念", "late core selection replaces the excerpt with exactly the chosen meaning");
        main.ResetLexicalDetails();
        Check(card.Visibility==Visibility.Collapsed && summary.Text=="", "translation and a subsequent query cannot inherit the previous overview");
    }
}
