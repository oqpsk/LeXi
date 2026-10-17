namespace Lexi.Core;

public static class LexicalCoverage
{
    private static readonly string[] Domains = ["cryptograph", "cryptocurrency", "cryptoasset", "blockchain", "web3", "defi",
        "information security", "cybersecurity", "trusted execution", "confidential computing", "加密资产", "加密货币", "区块链", "密码学", "信息安全", "可信执行", "机密计算"];
    public static bool Specialist(string text) => Domains.Any(d => text.Contains(d, StringComparison.OrdinalIgnoreCase));
    // Recall sampling only; never use favorite-domain tags to rank displayed meanings.
    public static int EvidenceRank(string text) => Specialist(text) ? 0 : SenseMerging.IsCurrent(text) ? 1 : 2;
    public static bool Abbreviation(AiSense sense) => sense.LexicalForm == "abbreviation" ||
        sense.Expansion.Length > 0 || LexicalLabels.PartOfSpeech(sense.PartOfSpeech) == "abbreviation";
    public static bool Compatible(AiSense a, AiSense b) => Abbreviation(a) == Abbreviation(b) &&
        (a.Expansion.Length == 0 || b.Expansion.Length == 0 || Queries.Normalize(a.Expansion) == Queries.Normalize(b.Expansion));
}
