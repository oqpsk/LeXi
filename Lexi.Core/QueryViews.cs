namespace Lexi.Core;

// Everything a front end needs to draw one query. Front ends own layout, focus, scrolling
// and disclosure state; wording, provenance, ordering and lifecycle decisions live here.
public enum QueryViewKind { Home, Dictionary, Translation }
public enum OriginalsMode { Keep, Collapse, Expand }

public sealed record NavigationView(bool CanBack, bool CanForward, string BackLabel, string ForwardLabel);

public sealed record LocalSourceView(string Name, string Headword, string Chinese, string English, string Original,
    string SourceUrl, string Attribution);
public sealed record OnlineSenseView(string Label, string Definition, OnlineExampleView[] Examples);
public sealed record OnlineExampleView(string Text, bool Chinese);
public sealed record OnlineSourceView(string Header, OnlineSenseView[] Senses, string Url, string Footer, bool Cached);

public sealed record SenseHeadingView(string Prefix, string PartOfSpeech, string Form, string Usage, bool Abbreviation,
    bool Provisional, string Tooltip);
public sealed record CitationView(int Number, string Label, string Definition, string Url);
public sealed record BriefView(string Caption, string Chinese, string English);
// Key is a content identity that survives streaming, reordering and merging. Front ends key
// their disclosure state by it (Key + ":examples", ":sources", ":alternatives").
public sealed record SenseView(string Key, SenseHeadingView Heading, string Expansion, string Chinese, string English,
    string Example, string ExampleChinese, string Provenance, string OriginalLabels, CitationView[] Citations, BriefView[] Briefs,
    string[] ReferenceKeys, string SourceNote, string AlternativesNote, string SourceLabel, SenseView[] Alternatives, string AlternativesLabel);
public sealed record ReferenceView(string Key, string Caption, string Expansion, string Chinese, string English,
    string SourceNote, string SourceName, string SourceUrl);
public sealed record NoticeView(string Text, bool Warning);
public sealed record AnswersView(bool Empty, ReferenceView[] References, SenseView[] Senses, NoticeView[] Notices,
    string[] Notes, string MergeWarning, OriginalsMode Originals);

public sealed record QuickReadView(bool Visible, string Text, string Tooltip, string State, bool ShowState, string Caption, string Explanation)
{
    public static readonly QuickReadView Hidden = new(false, "", "", "", false, "", "");
}
public sealed record WordFormCell(string Label, string Spelling, string Tooltip, bool Matches, string CopyText);
public sealed record WordFormRow(WordFormCell[] Cells);
public sealed record EtymologyView(string Text, string Caption, string LinkLabel, string SourceUrl, bool Generated);

public sealed record TranslationView(string Source, string Hint, string[] Paragraphs, string Note, bool Complete, string Error = "");

public sealed record QueryView(long Request, long Revision, QueryViewKind Kind, string Query, string Input, string SpeechText,
    string Heading, SavedPronunciation Pronunciation, QueryProgressState? Progress, long ProgressGeneration, string Status, bool CanRegenerate,
    NavigationView Navigation, LocalSourceView[] Local, string LocalHint, OnlineSourceView? Online, string OnlineHint,
    AnswersView? Answers, QuickReadView QuickRead, WordFormRow[] WordForms, EtymologyView[] Etymology, TranslationView? Translation);
