// Event — 事件卡定義：Trigger 觸發後跑 Stat 檢定（TN + 地塊修正），
// 依結果套用 Success/PartialSuccess/Failure 三分支敘事與 Effects。
// AllowedActionTypes 限制玩家在事件中可用的行動卡類型。
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
}
