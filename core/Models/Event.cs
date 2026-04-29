// Event — 事件卡定義：Trigger 觸發後跑 Stat 檢定（TN + 地塊修正），
// 依結果套用 Success/PartialSuccess/Failure 三分支敘事與 Effects。
// AllowedActionTypes 限制玩家在事件中可用的行動卡類型。
using System.Text.Json.Nodes;

namespace CardNarrative.Core.Models;

public sealed record EventOutcome(
    string Narrative,
    IReadOnlyList<EffectBase> Effects
);

public sealed record EventOutcomes(
    EventOutcome Success,
    EventOutcome PartialSuccess,
    EventOutcome Failure
);

public sealed record EventCard(
    string Id,
    string Name,
    EventType Type,
    int Tn,
    EventTrigger Trigger,
    Stat Stat,
    IReadOnlyList<ActionType> AllowedActionTypes,
    string Narrative,
    EventOutcomes Outcomes
)
{
    /// <summary>
    /// 模組自訂標籤。例：["random-filler"] 讓 D4 節奏保底機制從中抽選 filler 事件；
    /// ["climax"] 讓模組標記高潮事件；預設空陣列，舊 JSON 不需修改。
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Phase 3 任務 14（S3）· JsonLogic 揭露條件（規格書 §5.3 / §7.3）。
    /// null = 視為已揭露（缺省路徑：舊 JSON 無此欄位、行為不變）；
    /// 非 null 時 EventBroker 在嘗試觸發前評估，不通過則不 fire。
    /// 用於 A1–A5 前置條件：tilePlaced.X / hero.hp / event.X.outcome / hasEquipment.X / hasIntel.X。
    /// 模組 JSON 寫法：{ "revealCondition": { "and": [ ... ] } }
    /// </summary>
    public JsonNode? RevealCondition { get; init; } = null;
}
