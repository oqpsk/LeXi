using System.Text.RegularExpressions;

namespace Lexi.Core;

public static class LexicalLabels
{
    public static string PartOfSpeech(string raw)
    {
        var text = raw.Trim().ToLowerInvariant();
        if (text.Contains("proper noun")) return "专有名词";
        if (text.Contains("adverb") || Regex.IsMatch(text, @"^adv\.?$")) return "副词";
        if (text.Contains("pronoun") || Regex.IsMatch(text, @"^pron\.?$")) return "代词";
        if (text.Contains("adjective") || Regex.IsMatch(text, @"^(adj|a)\.?$")) return "形容词";
        if (text.Contains("verb") || Regex.IsMatch(text, @"^v[ti]?\.?$")) return "动词";
        if (text.Contains("noun") || Regex.IsMatch(text, @"^n\.?$")) return "名词";
        if (text.Contains("preposition") || text is "prep" or "prep.") return "介词";
        if (text.Contains("conjunction") || text is "conj" or "conj.") return "连词";
        if (text.Contains("interjection") || text is "int" or "int.") return "感叹词";
        if (text.Contains("phrase")) return "短语";
        if (text.Contains("determiner")) return "限定词";
        if (text.Contains("prefix")) return "前缀";
        if (text.Contains("suffix")) return "后缀";
        if (text.Contains("abbreviation") || text.Contains("acronym")) return "缩写";
        if (text.Contains("participle") || text.Contains("gerund")) return "动词形式";
        return Queries.HasChinese(raw) ? raw : "词语";
    }
    public static string Form(string raw)
    {
        var text = raw.ToLowerInvariant();
        var labels = new List<string>();
        if (text.Contains("gerund")) labels.Add("动名词");
        if (text.Contains("present participle")) labels.Add("现在分词");
        if (text.Contains("past participle")) labels.Add("过去分词");
        if (text.Contains("past tense")) labels.Add("过去式");
        if (text.Contains("intransitive")) labels.Add("不及物");
        else if (text.Contains("transitive")) labels.Add("及物");
        return string.Join(" / ", labels);
    }
    public static string Usage(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["standard"]="", ["neutral"]="", ["common"]="", ["general"]="", ["technical"]="专业用语", ["formal"]="书面语",
            ["informal"]="口语", ["colloquial"]="口语", ["slang"]="俚语", ["internet slang"]="网络俚语", ["gaming"]="游戏",
            ["derogatory"]="贬义", ["pejorative"]="贬义", ["offensive"]="冒犯用语", ["vulgar"]="粗俗用语", ["humorous"]="诙谐",
            ["archaic"]="古语", ["obsolete"]="旧时用语", ["rare"]="少见用法", ["literary"]="文学用语", ["dated"]="旧时用语",
            ["computing"]="计算机", ["computer science"]="计算机", ["information security"]="信息安全", ["geography"]="地理",
            ["nautical"]="航海", ["maritime"]="航海", ["mathematics"]="数学", ["physics"]="物理", ["medical"]="医学",
            ["anatomy"]="解剖学", ["biology"]="生物", ["chemistry"]="化学", ["law"]="法律", ["legal"]="法律", ["finance"]="金融",
            ["figurative"]="比喻", ["literal"]="字面含义", ["chiefly british"]="主要用于英式英语", ["british"]="英式英语", ["american"]="美式英语"
        };
        var parts = Regex.Split(raw, @"[,;，；·/]").Select(s => s.Trim()).Where(s => s.Length > 0);
        var labels = parts.Select(s => map.GetValueOrDefault(s, Queries.HasChinese(s) ? s : "")).Where(s => s.Length > 0).Distinct().ToList();
        if (labels.Count > 1) labels.Remove("专业用语");
        return string.Join(" · ", labels);
    }
}
