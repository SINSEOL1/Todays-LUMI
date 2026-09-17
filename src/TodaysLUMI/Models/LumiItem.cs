namespace TodaysLUMI.Models;

public sealed record LumiItem(string Key, string Name, string AccentHex)
{
    public static readonly LumiItem Meteorite = new("meteorite", "운석", "#405A86");
    public static readonly LumiItem TreeOfLife = new("tree-of-life", "생명의 나무", "#4C8A69");
    public static readonly LumiItem Mithril = new("mithril", "미스릴", "#4C8997");
    public static readonly LumiItem ForceCore = new("force-core", "포스 코어", "#765B9D");

    public static IReadOnlyList<LumiItem> All { get; } =
    [
        Meteorite,
        TreeOfLife,
        Mithril,
        ForceCore
    ];
}
