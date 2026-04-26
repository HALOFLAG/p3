// Tile — 地塊定義：Terrain、Important、AllowedActionTypes（限制手牌類型）、
// Resources（每次訪問可採集）、OnEnter 效果、Tags（放置規則 Plan B）。
// 放置規則：空 Tags 為中立橋接；非空 Tags 需與相鄰地塊至少一個標籤相符或相鄰中立地塊。
namespace CardNarrative.Core.Models;

public sealed record TileResource(
    string Key,
    string Name,
    int PerVisit
);

public sealed record Tile(
    string Id,
    string Name,
    Terrain Terrain,
    bool Important,
    IReadOnlyList<ActionType> AllowedActionTypes,
    IReadOnlyList<TileResource> Resources,
    IReadOnlyList<EffectBase> OnEnter
)
{
    // Placement rule (plan B):
    //   empty Tags     → neutral bridge, placeable anywhere adjacent; accepts any neighbor
    //   non-empty Tags → at least one adjacent placed tile must share a tag, or be neutral (empty Tags)
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    // Number of times this tile id is seeded into the tile deck. Each copy is
    // placed as its own 1x1 PlacedTile but the second / third / ... copies are
    // forced to be orthogonally adjacent to an already-placed tile of the same
    // id, producing a 2x1 / 3x1 / ... "long corridor" visual effect.
    // Copies > 1 occupy consecutive deck positions (drawn back-to-back).
    public int Copies { get; init; } = 1;

    /// <summary>
    /// A6 · 可互動地圖塊：對話 / 交易 / 祭壇 等玩家可選的互動項目。
    /// 每個 interaction 有獨立的 cost / effects / availability。
    /// </summary>
    public IReadOnlyList<TileInteraction> Interactions { get; init; } = Array.Empty<TileInteraction>();

    /// <summary>
    /// Phase 2 任務 11 Stage 2a：視覺對應 profile（規格書 §2.3 / §6.3）。
    /// 缺欄位時 runtime 走 fallback 表（Terrain → MapTerrain）。
    /// 用 <see cref="Map.TileVisualProfileResolver"/> 解析。
    /// </summary>
    public VisualProfile? VisualProfile { get; init; }
}
