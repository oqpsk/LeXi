using System.Text.RegularExpressions;

namespace Lexi.Core;

public static class LexicalLabels
{
    public static string PartOfSpeech(string raw)
    {
        var text = raw.Trim().ToLowerInvariant();
        if (text.Contains("proper noun") || text.Contains("专有名词")) return "proper noun";
        if (text.Contains("adverb") || text.Contains("副词") || Regex.IsMatch(text, @"^adv\.?$")) return "adverb";
        if (text.Contains("pronoun") || text.Contains("代词") || Regex.IsMatch(text, @"^pron\.?$")) return "pronoun";
        if (text.Contains("adjective") || text.Contains("形容词") || Regex.IsMatch(text, @"^(adj|a)\.?$")) return "adjective";
        if (text.Contains("verb") || text.Contains("动词") || Regex.IsMatch(text, @"^v[ti]?\.?$")) return "verb";
        if (text.Contains("noun") || text.Contains("名词") || Regex.IsMatch(text, @"^n\.?$")) return "noun";
        if (text.Contains("preposition") || text == "介词" || text is "prep" or "prep.") return "preposition";
        if (text.Contains("conjunction") || text == "连词" || text is "conj" or "conj.") return "conjunction";
        if (text.Contains("interjection") || text == "感叹词" || text is "int" or "int.") return "interjection";
        if (text.Contains("phrase") || text == "短语") return "phrase";
        if (text.Contains("determiner") || text == "限定词") return "determiner";
        if (text.Contains("prefix") || text == "前缀") return "prefix";
        if (text.Contains("suffix") || text == "后缀") return "suffix";
        if (text.Contains("abbreviation") || text.Contains("acronym") || text.Contains("initialism") || text == "缩写") return "abbreviation";
        if (text.Contains("participle") || text.Contains("gerund")) return "verb form";
        return "term";
    }
    public static string Form(string raw)
    {
        var text = raw.ToLowerInvariant();
        var labels = new List<string>();
        if (text.Contains("gerund")) labels.Add("gerund");
        if (text.Contains("present participle")) labels.Add("present participle");
        if (text.Contains("past participle")) labels.Add("past participle");
        if (text.Contains("past tense")) labels.Add("past tense");
        if (text.Contains("intransitive")) labels.Add("intransitive");
        else if (text.Contains("transitive")) labels.Add("transitive");
        return string.Join(" / ", labels);
    }
    public static string Usage(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["standard"]="", ["neutral"]="", ["common"]="", ["general"]="", ["technical"]="专业用语", ["formal"]="书面语",
            ["informal"]="口语", ["colloquial"]="口语", ["slang"]="俚语", ["internet slang"]="网络俚语", ["gaming"]="游戏",
            ["derogatory"]="贬义", ["pejorative"]="贬义", ["offensive"]="冒犯用语", ["vulgar"]="粗俗用语", ["humorous"]="诙谐",
            ["archaic"]="古语", ["obsolete"]="旧时用语", ["rare"]="少见用法", ["literary"]="文学用语", ["dated"]="旧时用语",
            ["cryptocurrency"]="加密资产", ["cryptoasset"]="加密资产", ["crypto"]="加密资产", ["blockchain"]="区块链",
            ["cryptography"]="密码学", ["web3"]="Web3", ["defi"]="去中心化金融", ["cybersecurity"]="信息安全",
            ["security"]="信息安全", ["confidential computing"]="机密计算", ["abbreviation"]="缩写", ["acronym"]="缩写", ["initialism"]="首字母缩写",
            ["computing"]="计算机", ["computer science"]="计算机", ["information security"]="信息安全", ["geography"]="地理",
            ["nautical"]="航海", ["maritime"]="航海", ["mathematics"]="数学", ["physics"]="物理", ["medical"]="医学",
            ["linguistics"]="语言学", ["medicine"]="医学", ["electronics"]="电子学", ["engineering"]="工程",
            ["software development"]="软件开发", ["software engineering"]="软件工程", ["data interchange"]="数据交换", ["data serialization"]="数据序列化", ["data format"]="数据格式", ["audio engineering"]="音频工程", ["radio engineering"]="无线电工程",
            ["anatomy"]="解剖学", ["biology"]="生物", ["chemistry"]="化学", ["law"]="法律", ["legal"]="法律", ["finance"]="金融",
            ["figurative"]="比喻", ["literal"]="字面含义", ["chiefly british"]="主要用于英式英语", ["british"]="英式英语", ["american"]="美式英语"
        };
        var parts = Regex.Split(raw, @"[,;，；·/]").Select(s => s.Trim()).Where(s => s.Length > 0);
        var labels = parts.Select(s => map.GetValueOrDefault(s, Queries.HasChinese(s) ? s : "")).Where(s => s.Length > 0).Distinct().ToList();
        if (labels.Count > 1) labels.Remove("专业用语");
        return string.Join(" · ", labels);
    }
}
