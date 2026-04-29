// Effects — 事件/卡牌產生的副作用（多型 record，JSON 以 type 辨識）。
// 類型：SetFlag、GrantEquipment、GrantResource、TileProgress、TriggerBattle、
// ClimaxTnIncrease、RollCheck（可含 OnFailure 巢狀）、DrawEncounter、
// TransformTile / TransformTilesByTag（B2 · 地圖塊轉變）。
// 用途：EffectHandler.Apply 逐項套用到 GameState。
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CardNarrative.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SetFlagEffect),             "setFlag")]
[JsonDerivedType(typeof(GrantEquipmentEffect),      "grantEquipment")]
[JsonDerivedType(typeof(GrantIntelEffect),          "grantIntel")]
[JsonDerivedType(typeof(GrantResourceEffect),       "grantResource")]
[JsonDerivedType(typeof(TileProgressEffect),        "tileProgress")]
[JsonDerivedType(typeof(TriggerBattleEffect),       "triggerBattle")]
[JsonDerivedType(typeof(ClimaxTnIncreaseEffect),    "climaxTnIncrease")]
[JsonDerivedType(typeof(RollCheckEffect),           "rollCheck")]
[JsonDerivedType(typeof(DrawEncounterEffect),       "drawEncounter")]
[JsonDerivedType(typeof(TransformTileEffect),       "transformTile")]
[JsonDerivedType(typeof(TransformTilesByTagEffect), "transformTilesByTag")]
public abstract record EffectBase;

public sealed record SetFlagEffect(string Key, JsonElement Value) : EffectBase;
public sealed record GrantEquipmentEffect(string Id) : EffectBase;

/// <summary>
/// Phase 3 任務 14（S4）· 取得情報效果（A5）。寫 GameState.AcquiredIntel；
/// 後續 JsonLogic 條件 hasIntel.&lt;id&gt; 即可命中；若情報含 UnlocksTags 則對應 tag 的 tile 可被放置。
/// </summary>
public sealed record GrantIntelEffect(string Id) : EffectBase;

public sealed record GrantResourceEffect(string Key, int Amount) : EffectBase;
public sealed record TileProgressEffect(int Delta) : EffectBase;
public sealed record TriggerBattleEffect(string BattleId) : EffectBase;
public sealed record ClimaxTnIncreaseEffect(int Amount) : EffectBase;
public sealed record RollCheckEffect(Stat Stat, int Tn, IReadOnlyList<EffectBase>? OnFailure = null) : EffectBase;
public sealed record DrawEncounterEffect() : EffectBase;

/// <summary>
/// B2 · 轉變指定座標的 tile 為 NewTileId。
/// X/Y 省略時取 CurrentPlayer.Position（TileEnter 事件典型用法）。
/// PreservePlayerPosition=true（預設）：玩家若站在該格，留在原位繼續遊戲；
/// false：嘗試推到相鄰已放置的格，若找不到仍保持原位。
/// 轉變後 Level 重置為 Unknown、清除資源採集紀錄 / interaction usage / action-card-once 旗標。
/// </summary>
public sealed record TransformTileEffect(
    string NewTileId,
    int? X = null,
    int? Y = null,
    bool PreservePlayerPosition = true
) : EffectBase;

/// <summary>
/// B2 · 批次轉變地圖上 Tags 全部符合的 tile 為 NewTileId（AND 語義）。
/// 災難性事件用法（例：暴雨沖毀所有 tags=["road","lowland"] 的 tile）。
/// 玩家位置永遠保留（批次範圍廣、不做推擠）。
/// </summary>
public sealed record TransformTilesByTagEffect(
    IReadOnlyList<string> Tags,
    string NewTileId
) : EffectBase;
