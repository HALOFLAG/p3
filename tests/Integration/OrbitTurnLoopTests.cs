// L3 整合測試 — TurnLoop × ORBIT 跨模組劇本（規格 §1.6）。
//
// 涵蓋：
//   L3-08「事件連鎖 5 層上限」— 構造 6 個 ClassA 事件全部 reveal-OK，
//                              第 6 個應被 DeferToNext，本回合僅觸發 5 個。
//   L3-09「結局卡 reveal → trigger → 結束遊戲」— 結局卡 EndingTriggered 應反映於
//                                                  TurnLoop.PendingEvent，呼叫者可據此走結算流程。
//
// 註：本測試走「手動 RegisterEventToOrbit」途徑——尚未做事件來源 → ORBIT 自動 register（留待後續 PR）。
using System.Text.Json.Nodes;
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Integration;

public class OrbitTurnLoopTests
{
    private static EventCard MakeCard(string id) => new(
        Id: id,
        Name: id,
        Type: EventType.Exploration,
        Tn: 10,
        Trigger: new TileEnterTrigger("any"),
        Stat: Stat.Skill,
        AllowedActionTypes: new[] { ActionType.Exploration },
        Narrative: "n",
        Outcomes: new EventOutcomes(
            Success: new EventOutcome("ok", Array.Empty<EffectBase>()),
            PartialSuccess: new EventOutcome("mid", Array.Empty<EffectBase>()),
            Failure: new EventOutcome("bad", Array.Empty<EffectBase>())));

    [Fact]
    public void L3_08_ChainDepthFive_DefersExtrasToNextTurn()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        var loop = new TurnLoop(state, new SeededDiceService(1), module, orbit: resolver);

        // 6 張無條件可觸發 (reveal=null,trigger=null → 都 true) 的 A 類事件
        for (int i = 0; i < 6; i++)
        {
            var inst = new EventInstance(
                MakeCard($"chain-{i}"),
                initialClass: EventOrbitClass.ClassA);
            orbit.Push(inst);
        }

        // 走到 EventCheck：Draw → Action → 玩家不動，直接 Advance 進入 EventCheck
        loop.Advance(); // Draw → Action
        loop.State.Phase.Should().Be(TurnPhase.Action);
        loop.Advance(); // Action → EventCheck → ORBIT 觸發

        // 第 1 張進 PendingEvent；其餘 (應為 4 張，連鎖深度 5 - 第 1 張 = 4) 排入 PendingEventQueue
        loop.PendingEvent.Should().NotBeNull();
        loop.PendingEvent!.Id.Should().StartWith("chain-");
        // 5 層上限 → 共觸發 5 張；剩 1 張被 deferred
        (1 + state.PendingEventQueue.Count).Should().Be(5);
        orbit.DeferredToNext.Should().HaveCount(1, "第 6 張超出連鎖深度 5，應被推遲到下回合");
    }

    [Fact]
    public void L3_09_EndingClassA_StopsResolutionAndSetsPendingEvent()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        var loop = new TurnLoop(state, new SeededDiceService(1), module, orbit: resolver);

        // 結局卡 + 一張普通 A 類事件；結局卡優先於普通事件處理
        var endingInst = new EventInstance(
            MakeCard("ending-true"),
            initialClass: EventOrbitClass.ClassA,
            isEnding: true);
        orbit.Push(endingInst);
        orbit.Push(new EventInstance(MakeCard("regular-a"), initialClass: EventOrbitClass.ClassA));

        loop.Advance(); // Draw → Action
        loop.Advance(); // Action → EventCheck → ORBIT

        loop.PendingEvent.Should().NotBeNull();
        loop.PendingEvent!.Id.Should().Be("ending-true");
        // 結局觸發 → 主迴圈中止；regular-a 仍留在 ORBIT pending（未被 trigger 也未 deferred）
        orbit.Pending.Should().ContainSingle(i => i.Id == "regular-a");
    }

    [Fact]
    public void RevealAndTriggerConditions_GovernPromotionThroughOrbit()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module, players: 1);
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        var loop = new TurnLoop(state, new SeededDiceService(1), module, orbit: resolver);

        // 條件事件：reveal 取 flag.story_started == true；trigger 取 turn >= 2
        var inst = new EventInstance(
            MakeCard("conditional"),
            revealCondition: JsonNode.Parse("""{"==":[{"var":"flag.story_started"},true]}"""),
            triggerCondition: JsonNode.Parse("""{">=":[{"var":"turn"},2]}"""));
        orbit.Push(inst);

        // turn 1：flag 還沒設 → reveal=false → 不觸發
        loop.Advance(); // Draw → Action
        loop.Advance(); // Action → EventCheck → 沒事 → 自動進 MapExpand 或 TurnEnd
        loop.PendingEvent.Should().BeNull();
        inst.Class.Should().Be(EventOrbitClass.ClassC);
    }
}
