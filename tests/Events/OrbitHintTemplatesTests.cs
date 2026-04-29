// OrbitHintTemplatesTests — Phase 3 任務 14（S7）。
//
// 驗證 hint 模板輸出（trigger 5 類 + reveal 條件淺解析常見 pattern）。
using System.Text.Json.Nodes;
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Tests.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Events;

public class OrbitHintTemplatesTests
{
    private static EventCard MakeEvent(EventTrigger trigger, string? revealJson = null) => new(
        Id: "test-ev",
        Name: "test",
        Type: EventType.Exploration,
        Tn: 8,
        Trigger: trigger,
        Stat: Stat.Skill,
        AllowedActionTypes: System.Array.Empty<ActionType>(),
        Narrative: "n",
        Outcomes: new EventOutcomes(
            Success: new EventOutcome("s", System.Array.Empty<EffectBase>()),
            PartialSuccess: new EventOutcome("p", System.Array.Empty<EffectBase>()),
            Failure: new EventOutcome("f", System.Array.Empty<EffectBase>())))
    {
        RevealCondition = revealJson is null ? null : JsonNode.Parse(revealJson),
    };

    // ─── Trigger 模板 ──────────────────────────────────

    [Fact]
    public void TileEnterTrigger_UsesTileName()
    {
        var module = ModuleFactory.Load(); // 含 "warehouse" tile，name="倉庫"
        var ev = MakeEvent(new TileEnterTrigger("warehouse"));
        OrbitHintTemplates.Build(ev, module).Should().Contain("到達");
        OrbitHintTemplates.Build(ev, module).Should().Contain("時觸發");
    }

    [Fact]
    public void TurnAtTrigger_UsesRoundNumber()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(new TurnAtTrigger(7));
        OrbitHintTemplates.Build(ev, module).Should().Be("第 7 回合行動階段觸發");
    }

    [Fact]
    public void TurnRangeTrigger_BothEnds()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(new TurnRangeTrigger(3, 5));
        OrbitHintTemplates.Build(ev, module).Should().Be("第 3–5 回合可觸發");
    }

    [Fact]
    public void TurnRangeTrigger_OpenEnd()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(new TurnRangeTrigger(10, null));
        OrbitHintTemplates.Build(ev, module).Should().Be("第 10 回合起可觸發");
    }

    [Fact]
    public void PlayerActionTrigger_NoCount()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(new PlayerActionTrigger(PlayerActionKind.Observe, null));
        OrbitHintTemplates.Build(ev, module).Should().Be("進行『觀察』時觸發");
    }

    [Fact]
    public void PlayerActionTrigger_WithCount()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(new PlayerActionTrigger(PlayerActionKind.Observe, 3));
        OrbitHintTemplates.Build(ev, module).Should().Be("進行 3 次觀察後觸發");
    }

    // ─── RevealCondition 摘要 ───────────────────────────

    [Fact]
    public void Reveal_TilePlacedById()
    {
        var module = ModuleFactory.Load(); // 含 warehouse
        var ev = MakeEvent(
            new TurnAtTrigger(1),
            """{"var":"tilePlaced.warehouse"}""");
        var hint = OrbitHintTemplates.Build(ev, module);
        hint.Should().Contain("第 1 回合行動階段觸發");
        hint.Should().Contain("需先放置");
    }

    [Fact]
    public void Reveal_HasIntel()
    {
        // 用 abandoned-mansion 才有 intel "underground-passage"
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var result = loader.Load(TestPaths.AbandonedMansionFolder);
        var module = ((ModuleLoadResult.Success)result).Module;

        var ev = MakeEvent(
            new TurnAtTrigger(1),
            """{"var":"hasIntel.underground-passage"}""");
        var hint = OrbitHintTemplates.Build(ev, module);
        hint.Should().Contain("教堂下的地下道");
        hint.Should().Contain("需取得情報");
    }

    [Fact]
    public void Reveal_EventConsumed()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(
            new TurnAtTrigger(1),
            """{"var":"event.warehouse-investigation.consumed"}""");
        var hint = OrbitHintTemplates.Build(ev, module);
        hint.Should().Contain("需先完成");
    }

    [Fact]
    public void Reveal_HpComparison()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(
            new TurnAtTrigger(1),
            """{">=":[{"var":"hero.hp"},5]}""");
        var hint = OrbitHintTemplates.Build(ev, module);
        hint.Should().Contain("HP");
        hint.Should().Contain(">=");
        hint.Should().Contain("5");
    }

    [Fact]
    public void Reveal_EventOutcomeEqualsSuccess()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(
            new TurnAtTrigger(1),
            """{"==":[{"var":"event.warehouse-investigation.outcome"},"success"]}""");
        var hint = OrbitHintTemplates.Build(ev, module);
        hint.Should().Contain("需成功");
    }

    [Fact]
    public void Reveal_AndCombination()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(
            new TileEnterTrigger("warehouse"),
            """{"and":[{"var":"tilePlaced.warehouse"},{">=":[{"var":"turn"},3]}]}""");
        var hint = OrbitHintTemplates.Build(ev, module);
        hint.Should().Contain("需先放置");
        hint.Should().Contain("回合數");
    }

    [Fact]
    public void Reveal_UnknownPattern_ReturnsOpaque()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(
            new TurnAtTrigger(1),
            """{"in":["foo","bar"]}""");
        var hint = OrbitHintTemplates.Build(ev, module);
        hint.Should().Contain("(隱藏條件)");
    }

    [Fact]
    public void Reveal_NoRevealCondition_OnlyTrigger()
    {
        var module = ModuleFactory.Load();
        var ev = MakeEvent(new TurnAtTrigger(7)); // no reveal
        OrbitHintTemplates.Build(ev, module).Should().Be("第 7 回合行動階段觸發");
    }
}
