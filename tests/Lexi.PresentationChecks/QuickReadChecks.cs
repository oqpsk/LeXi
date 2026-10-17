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
        var guide = (FrameworkElement)main.FindName("QuickReadGuide");
        var state = (TextBlock)main.FindName("QuickReadState");
        var glyph = (System.Windows.Shapes.Path)main.FindName("QuickReadGlyph");
        var mark = glyph.Data;
        string Caption() => System.Windows.Automation.AutomationProperties.GetName(guide);
        main.BeginLexicalOverview("TEE", []);
        Check(Contents(ai,true).OfType<TextBox>().Count(t=>t.Text==brief.Chinese)==1 && summary.Text.Contains("可信执行环境") && Caption().StartsWith("TL;DR") && ReferenceEquals(glyph.Data, mark),
            "offline specialist lookup has one attributed definition row and an immediate overview");
        Check(!Contents((DependencyObject)main.Content,true).OfType<Expander>().Any(e=>e.Header.ToString()!.Contains("随应用收录")),
            "specialist references no longer occupy a standalone section");
        main.RenderAnswers(snapshot,evidence,false);
        Check(summary.Text=="可信执行环境" && Caption().StartsWith("TL;DR") && Caption().Contains("预览") && ReferenceEquals(glyph.Data, mark),
            "completed model gloss is concise but stays provisional until the query is finalized");
        Check(Contents(ai,true).OfType<TextBox>().Count(t=>t.Text==brief.Chinese)==1,
            "a covered specialist definition replaces its fallback instead of appearing twice");
        var streaming = answer with {Phase=ModelPhase.Streaming};
        main.RenderAnswers(new([streaming],SenseMerging.Group([streaming])),evidence,false);
        Check(summary.Text=="可信执行环境" && Caption().StartsWith("TL;DR") && Caption().Contains("预览") && ReferenceEquals(glyph.Data, mark), "partial refreshes keep an already completed overview until replacement or failure");
        main.RenderAnswers(snapshot,evidence,false);
        var source = Contents(ai,true).OfType<ToggleButton>().Single(t=>t.Content.ToString()!.StartsWith("参考资料"));
        source.IsChecked=true;
        Check(Contents(ai,true).OfType<TextBox>().Count(t=>t.Text==brief.Chinese)==2,
            "opening the sense references reveals the original brief and its attribution");
        main.RenderAnswers(snapshot,evidence,true);
        Check(Caption().Contains("已整理") && Contents(ai,true).OfType<ToggleButton>().Single(t=>t.Content.ToString()!.StartsWith("参考资料")).IsChecked==true,
            "finalized summary and reference disclosure retain their independent states");
        var failed = new ModelAnswer(model,ModelPhase.Failed,null,false,"fixture failure");
        main.RenderAnswers(new([failed],[]),evidence);
        Check(Contents(ai,true).OfType<TextBox>().Any(t=>t.Text==brief.Chinese) && Caption().Contains("词典摘录"),
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
        Check(Caption().Contains("部分结果") && state.Visibility==Visibility.Visible,"a failed merge never marks the overview as fully organized");
        main.RenderAnswers(snapshot,evidence);
        var card = (FrameworkElement)main.FindName("QuickReadCard");
        foreach (var width in new[] {558,718,1078})
        {
            Measure((FrameworkElement)main.Content,width,618);
            Check(card.DesiredSize.Height<=70 && summary.FontSize>=19 && summary.IsReadOnly && state.Visibility==Visibility.Collapsed &&
                guide.ActualHeight==28 && glyph.ActualWidth==20 && glyph.ActualHeight==20 &&
                Math.Abs(guide.TranslatePoint(new Point(0,guide.ActualHeight/2),card).Y-summary.TranslatePoint(new Point(0,summary.ActualHeight/2),card).Y)<0.1 &&
                guide.TranslatePoint(new Point(guide.ActualWidth,0),card).X<summary.TranslatePoint(new Point(),card).X,
                $"compact selectable overview stays readable at {width+2} DIP");
        }
        summary.Text = "这是用来检查换行与居中的较长摘要。" + string.Concat(Enumerable.Repeat("完整释义可以换行阅读，固定标记与正文整体中心对齐。", 3));
        Measure((FrameworkElement)main.Content,558,618);
        Check(summary.ActualHeight>28 && Math.Abs(guide.TranslatePoint(new Point(0,guide.ActualHeight/2),card).Y-
            summary.TranslatePoint(new Point(0,summary.ActualHeight/2),card).Y)<0.6,
            "fixed TLDR badge stays vertically centered beside a wrapped overview");
        Check(mark.Bounds.Width==20 && mark.Bounds.Height==20 && !Contents(guide,true).OfType<TextBlock>().Any(),
            "TLDR uses fixed square vector letterforms with no font-dependent row widths");
        Check(((Border)guide).Background==main.FindResource("Accent") && glyph.Fill==main.FindResource("Canvas"),
            "TLDR badge uses the shared pale pink and contrasting dark letterforms");
        var legacy = answer with { Entry=answer.Entry! with {Senses=[sense, sense with {Chinese="另一个概念",Gloss="另一个概念",Expansion="Other concept"}]} };
        main.RenderAnswers(new([legacy],SenseMerging.Group([legacy])),evidence);
        Check(Caption().StartsWith("TL;DR") && guide.ToolTip.ToString()!.Contains("尚未选定核心含义") && ReferenceEquals(glyph.Data, mark), "unranked excerpts keep the TLDR badge while disclosing their selection status");
        var request = SenseMerging.Request("TEE",[legacy]);
        var plan = new SenseMergePlan(request.InputHash,request.Pairs.Select(p=>new SenseVerdict(p.A,p.B,"different",1)).ToArray(),["m0s1"]);
        main.RenderAnswers(new([legacy],SenseMerging.Group([legacy],plan)),evidence);
        Check(Caption()=="TL;DR · 已整理" && ReferenceEquals(glyph.Data, mark) && summary.Text=="另一个概念", "late core selection replaces the excerpt with exactly the chosen meaning");
        main.ResetLexicalDetails();
        Check(card.Visibility==Visibility.Collapsed && summary.Text=="", "translation and a subsequent query cannot inherit the previous overview");
    }
}
