// EventInstance — ORBIT 軌道上的事件實例（含結局卡）。
// 包裝 EventCard + 動態 Class 狀態 + reveal/trigger JsonLogic 規則。
// 由 EventOrbit 持有；條件評估在 EventResolver/JsonLogicEvaluator 進行。
//
// 注意：IsEnding 為 true 時表示這是結局卡（A 類觸發 → 直接結束遊戲，§1.6）。
// RevealCondition / TriggerCondition 為 null 時：
//   - RevealCondition == null → 視為已揭露（直接進 ClassB / ClassA 評估）
//   - TriggerCondition == null → reveal 通過後直接視為 ClassA
using System.Text.Json.Nodes;
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Events;

public sealed class EventInstance
{
    public EventCard Card { get; }
    public EventOrbitClass Class { get; set; }
    public int RevealedTurn { get; set; }
    public JsonNode? RevealCondition { get; }
    public JsonNode? TriggerCondition { get; }
    public bool IsEnding { get; }

    public string Id => Card.Id;

    public EventInstance(
        EventCard card,
        JsonNode? revealCondition = null,
        JsonNode? triggerCondition = null,
        bool isEnding = false,
        EventOrbitClass initialClass = EventOrbitClass.Hidden,
        int revealedTurn = 0)
    {
        Card = card;
        RevealCondition = revealCondition;
        TriggerCondition = triggerCondition;
        IsEnding = isEnding;
        Class = initialClass;
        RevealedTurn = revealedTurn;
    }
}
