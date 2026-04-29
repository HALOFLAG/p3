// EventScheduler — 依玩家進入地塊 / 回合推進事件檢查階段尋找可觸發事件。
// 過濾已 Consumed 事件；支援 TileEnter / Flag / TurnTimer / ActionCount 四種 Trigger。
//
// Phase 3 任務 14（S2）起：runtime 已改用 <see cref="EventBroker"/> 統一處理；
// S6 起整類標 [Obsolete] —— 仍保留供既有 TurnLoop 路徑與 xUnit 既有測試使用，
// 不再為新 trigger 類型擴充（playerAction / turnAt / turnRange 都只走 broker）。
using System.Text.Json;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

#pragma warning disable CS0618 // TurnTimerTrigger / ActionCountTrigger 已標 Obsolete；舊路徑保留評估行為

namespace CardNarrative.Core.Services;

[Obsolete("Use EventBroker instead. EventScheduler is retained only for legacy TurnLoop tests; will be removed once TurnLoop migrates to EventBroker.")]
public sealed class EventScheduler
{
    public EventCard? FindTriggeredOnEnterTile(string tileId, GameState state, Module module)
    {
        foreach (var e in module.Events.Values)
        {
            if (state.ConsumedEventIds.Contains(e.Id)) continue;
            if (e.Trigger is TileEnterTrigger t && t.TileId == tileId)
                return e;
        }
        return null;
    }

    public EventCard? FindTriggeredOnEventCheck(GameState state, Module module)
    {
        foreach (var e in module.Events.Values)
        {
            if (state.ConsumedEventIds.Contains(e.Id)) continue;
            switch (e.Trigger)
            {
                case FlagTrigger f:
                    if (state.Flags.TryGetValue(f.Key, out var v) && JsonElementEquals(v, f.Value))
                        return e;
                    break;
                case TurnTimerTrigger t:
                    if (t.Interval > 0 && state.CurrentBigRound > 0 && state.CurrentBigRound % t.Interval == 0)
                        return e;
                    break;
            }
        }
        return null;
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        return a.ValueKind switch
        {
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            _ => a.GetRawText() == b.GetRawText()
        };
    }
}
