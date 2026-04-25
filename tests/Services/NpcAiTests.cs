using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class NpcAiTests
{
    [Fact]
    public void SuggestActionCard_SceneMatch_FindsTaggedCard()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Hand.Add("careful-search"); // has "search" tag
        var npc = module.NpcCompanions["companion-thief"];
        var ai = new NpcAi();

        var id = ai.SuggestActionCardId(npc, "exploration", module, state.CurrentPlayer);

        id.Should().Be("careful-search");
    }

    [Fact]
    public void SuggestActionCard_UnknownScene_FallsBackToDefault()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Hand.Add("integrate-intel"); // type=thinking, tag=analysis
        var npc = module.NpcCompanions["companion-thief"];
        var ai = new NpcAi();

        // default scene preferredActionTags: ["thinking"] — matches card type label? Actually matches tag, not type.
        // integrate-intel has tag "analysis" which does not match "thinking" tag.
        var id = ai.SuggestActionCardId(npc, "unknown-scene", module, state.CurrentPlayer);

        // No tag match → null is expected here since default preferredActionTags=["thinking"] and no card has tag "thinking"
        id.Should().BeNull();
    }

    [Fact]
    public void SuggestActionCard_EmptyHand_ReturnsNull()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Hand.Clear();
        var npc = module.NpcCompanions["companion-thief"];
        var ai = new NpcAi();

        ai.SuggestActionCardId(npc, "battle", module, state.CurrentPlayer).Should().BeNull();
    }

    [Fact]
    public void SuggestCandidates_RankedByScoreDescending()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Hand.Clear();
        // careful-search has tags [search, perception] — 2 matches for exploration scene
        // persuade has [persuade] — 0 matches for exploration
        state.CurrentPlayer.Hand.Add("careful-search");
        state.CurrentPlayer.Hand.Add("persuade");
        var npc = module.NpcCompanions["companion-thief"];
        var ai = new NpcAi();

        var list = ai.SuggestCandidates(npc, "exploration", module, state.CurrentPlayer);

        list.Should().NotBeEmpty();
        list[0].CardId.Should().Be("careful-search");
        list[0].Score.Should().Be(2);
    }

    [Fact]
    public void SuggestCandidates_IncludesSceneAndTagsInReason()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Hand.Clear();
        state.CurrentPlayer.Hand.Add("careful-search");
        var npc = module.NpcCompanions["companion-thief"];
        var ai = new NpcAi();

        var list = ai.SuggestCandidates(npc, "exploration", module, state.CurrentPlayer);

        list[0].Reason.Should().Contain("exploration");
        list[0].Reason.Should().Contain("search");
    }

    [Fact]
    public void SuggestCandidates_RespectsMaxResults()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Hand.Clear();
        state.CurrentPlayer.Hand.Add("careful-search");
        var npc = module.NpcCompanions["companion-thief"];
        var ai = new NpcAi();

        var list = ai.SuggestCandidates(npc, "exploration", module, state.CurrentPlayer, maxResults: 1);

        list.Count.Should().BeLessOrEqualTo(1);
    }

    [Fact]
    public void SuggestCandidates_EmptyHand_ReturnsEmpty()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.CurrentPlayer.Hand.Clear();
        var npc = module.NpcCompanions["companion-thief"];
        var ai = new NpcAi();

        var list = ai.SuggestCandidates(npc, "exploration", module, state.CurrentPlayer);

        list.Should().BeEmpty();
    }
}
