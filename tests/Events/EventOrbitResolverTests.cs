// EventOrbitResolverTests — ORBIT 解析鏈測試（規格 §1.6）。
// 涵蓋：reveal → trigger 升降、A→B→C 排序處理、連鎖深度上限 5、
//      同 ID 同 Check 僅觸發 1 次、結局卡 reveal 在 EventCheck 開頭/結尾各評估、
//      結局卡 ClassA 觸發 → 報告 EndingTriggered 並停止剩餘 queue。
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using FluentAssertions;

namespace CardNarrative.Tests.Events;

public class EventOrbitResolverTests
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
    public void RevealAndTrigger_BothPass_PromotesCtoA_AndTriggers()
    {
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        resolver.RegisterEventToOrbit(
            MakeCard("e1"),
            revealConditionJson: "{\"==\":[{\"var\":\"flag.revealed\"},true]}",
            triggerConditionJson: "{\"==\":[{\"var\":\"flag.ready\"},true]}");

        var ctx = new JsonLogicContext().Set("flag.revealed", true).Set("flag.ready", true);
        var triggered = new List<string>();

        var report = resolver.ResolvePending(ctx, inst => triggered.Add(inst.Id));

        triggered.Should().Equal("e1");
        report.Triggered.Select(i => i.Id).Should().Equal("e1");
        orbit.Pending.Should().BeEmpty();
    }

    [Fact]
    public void RevealPasses_TriggerFails_StopsAtClassB()
    {
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        resolver.RegisterEventToOrbit(
            MakeCard("e1"),
            revealConditionJson: "true",
            triggerConditionJson: "{\"==\":[{\"var\":\"flag.ready\"},true]}");

        var ctx = new JsonLogicContext(); // flag.ready 缺 → trigger false
        var triggered = new List<string>();

        var report = resolver.ResolvePending(ctx, inst => triggered.Add(inst.Id));

        triggered.Should().BeEmpty();
        report.Triggered.Should().BeEmpty();
        orbit.Pending.Should().HaveCount(1);
        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassB);
    }

    [Fact]
    public void SameEventId_OnlyTriggeredOncePerCheck()
    {
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        // 兩個 instance 同 id → 模擬同 ID 在同 Check 重複出現
        resolver.RegisterEventToOrbit(MakeCard("dup"), "true", "true");
        resolver.RegisterEventToOrbit(MakeCard("dup"), "true", "true");

        var triggered = new List<string>();
        resolver.ResolvePending(new JsonLogicContext(), inst => triggered.Add(inst.Id));

        triggered.Should().HaveCount(1).And.Equal("dup");
    }

    [Fact]
    public void ChainDepth_ExceedsFive_DefersRemainingToNextTurn()
    {
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        // 7 個皆會立即升 A
        for (int i = 0; i < 7; i++)
            resolver.RegisterEventToOrbit(MakeCard($"e{i}"), "true", "true");

        var report = resolver.ResolvePending(new JsonLogicContext());

        // 規格 §1.6：連鎖深度上限 5
        report.Triggered.Should().HaveCount(EventOrbit.MaxChainDepth);
        report.Deferred.Should().HaveCount(7 - EventOrbit.MaxChainDepth);
        orbit.DeferredToNext.Should().HaveCount(report.Deferred.Count);
    }

    [Fact]
    public void DeferredEvents_RestoredToFrontOfQueue_OnNextResolve()
    {
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        for (int i = 0; i < 7; i++)
            resolver.RegisterEventToOrbit(MakeCard($"e{i}"), "true", "true");

        resolver.ResolvePending(new JsonLogicContext());
        var deferredIds = orbit.DeferredToNext.Select(i => i.Id).ToArray();
        deferredIds.Should().NotBeEmpty();

        // 下回合：deferred 應插回最前
        var report2 = resolver.ResolvePending(new JsonLogicContext());
        report2.Triggered.Select(i => i.Id).Take(deferredIds.Length).Should().Equal(deferredIds);
    }

    [Fact]
    public void EndingCard_ClassA_TriggersEndingAndStopsRemaining()
    {
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        // 一個一般 A、一個結局 A → 結局優先處理且結束剩餘
        resolver.RegisterEventToOrbit(MakeCard("normal"), "true", "true");
        resolver.RegisterEventToOrbit(MakeCard("ending"), "true", "true", isEnding: true);

        var triggered = new List<string>();
        var report = resolver.ResolvePending(new JsonLogicContext(), inst => triggered.Add(inst.Id));

        // 結局卡優先（OrderByDescending IsEnding）
        triggered[0].Should().Be("ending");
        report.EndingTriggered.Should().NotBeNull();
        report.EndingTriggered!.Id.Should().Be("ending");
        // 結局觸發後迴圈 break；normal 仍留在 Pending
        orbit.Pending.Select(i => i.Id).Should().Contain("normal");
    }

    [Fact]
    public void EndingReveal_IsEvaluatedAtStartAndEnd_OfEventCheck()
    {
        // 起始 reveal 條件不成立；處理一般事件後設 flag → 結尾 reveal 才通過
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        resolver.RegisterEventToOrbit(
            MakeCard("ending"),
            revealConditionJson: "{\"==\":[{\"var\":\"flag.endingUnlocked\"},true]}",
            triggerConditionJson: "false", // 不會自動觸發，僅測 reveal 升 B
            isEnding: true);
        resolver.RegisterEventToOrbit(MakeCard("trigger_unlock"), "true", "true");

        var ctx = new JsonLogicContext();
        // 處理 trigger_unlock 後在 onTrigger 中改變 ctx；模擬「副作用設旗標」
        resolver.ResolvePending(ctx, inst =>
        {
            if (inst.Id == "trigger_unlock")
                ctx.Set("flag.endingUnlocked", true);
        });

        // ending 卡在「結尾再評估 reveal」應升 B（reveal 通過、trigger=false）
        var ending = orbit.Pending.Single(i => i.Id == "ending");
        ending.Class.Should().Be(EventOrbitClass.ClassB);
    }

    [Fact]
    public void ProcessingA_ReevaluatesB_OnSubsequentIterations()
    {
        // B 一開始 trigger=false；處理 A 後改變 ctx → B 升 A 並被觸發
        var orbit = new EventOrbit();
        var resolver = new EventOrbitResolver(orbit);
        resolver.RegisterEventToOrbit(MakeCard("a"), "true", "true");
        resolver.RegisterEventToOrbit(
            MakeCard("b"),
            revealConditionJson: "true",
            triggerConditionJson: "{\"==\":[{\"var\":\"flag.unlocked\"},true]}");

        var ctx = new JsonLogicContext();
        var triggered = new List<string>();

        resolver.ResolvePending(ctx, inst =>
        {
            triggered.Add(inst.Id);
            if (inst.Id == "a") ctx.Set("flag.unlocked", true);
        });

        triggered.Should().Equal("a", "b");
    }
}
