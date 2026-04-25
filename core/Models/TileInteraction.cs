// TileInteraction — A6 · 可互動地圖塊。
// 每個 tile 可定義 0..N 個 interactions（商店、對話、祭壇等），玩家在 Action 階段
// 透過 ActionDock 觸發。費用與效果複用既有 Effect 系統。
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TileInteractionAvailability
{
    /// <summary>無限次（每次進入 tile 可重複使用）。</summary>
    Unlimited,
    /// <summary>每次訪問 1 次（離開 tile 後重置）。</summary>
    OncePerVisit,
    /// <summary>每大回合 1 次（大回合推進時重置）。</summary>
    OncePerBigRound,
    /// <summary>整局 1 次（永久記錄）。</summary>
    OncePerGame
}

/// <summary>
/// 互動成本。null 欄位表示不需要該種資源。
/// Resources 支援多資源 AND 扣除（例：2 clue_shard + 1 herb）。
/// EquipmentIds 支援多裝備 AND 丟棄（例：需要丟棄特定裝備換取效果）。
/// </summary>
public sealed record TileInteractionCost(
    IReadOnlyDictionary<string, int>? Resources = null,
    IReadOnlyList<string>? EquipmentIds = null);

public sealed record TileInteraction(
    string Id,
    string Name,
    string Description,
    TileInteractionCost? Cost,
    IReadOnlyList<EffectBase> Effects,
    TileInteractionAvailability Availability);
