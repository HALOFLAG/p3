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

    /// <summary>
    /// Phase 2 任務 11 Stage 5：tile-side 轉變規則（規格書 §1.5 TileTransformRule）。
    /// 模組載入時透過 <see cref="Map.TileTransformRegistry"/> 索引；
    /// EventCheck 階段事件 trigger 後逐項評估 condition、符合則套 TransformTileEffect。
    /// 缺欄位 = 該 tile 對外部事件無 transform 規則。
    /// </summary>
    public IReadOnlyList<TileTransformRule> Transformations { get; init; } = Array.Empty<TileTransformRule>();

    /// <summary>
    /// v1.12 Stage 6 — 「地塊組」連續放置：1 張卡 = N 個 1×1 cell，玩家強制連續放完 N 格。
    /// 預設 1（單格 tile，行為與既有相同）。Stage 6 階段純 N-cell 連通邏輯；
    /// Stage 7 起搭配 <see cref="GroupShape"/> 強制矩形/直線拓撲。
    /// </summary>
    public int GroupCount { get; init; } = 1;

    /// <summary>
    /// v1.12 Stage 6/7 — 群組形狀模板（如 "rectangle:2x2"、"line:2"）。
    /// null 或空字串 = 自由連通（任何相鄰已放同組格皆可）；
    /// 已設定 = Stage 7 起強制依形狀限定下一格位置。
    /// </summary>
    public string? GroupShape { get; init; }
}
