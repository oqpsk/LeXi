namespace Lexi.Core;

// Labels for the online switch and regeneration action, shared so both front ends describe
// the same behavior. Keyboard shortcut hints are added by each platform.
public static class ControlText
{
    public static string OnlineLabel(bool online) => online ? "在线补充" : "离线查询";
    public static string OnlineTooltip(bool online) => online ? "在线补充已开启 · 点击关闭，仅使用本地词库与缓存" : "仅离线 · 点击开启在线词典与模型补充";
    public static string RegenerateTooltip(bool online) => online ? "重新查询并生成结果，跳过本次查询的已有缓存" : "开启在线补充后可重新生成";
}
