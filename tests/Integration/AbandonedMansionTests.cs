using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Integration;

public class AbandonedMansionTests
{
    private ModuleLoader NewLoader() => new(TestPaths.SchemasFolder);

    [Fact]
    public void AbandonedMansion_Loads_Successfully()
    {
        var result = NewLoader().Load(TestPaths.AbandonedMansionFolder);

        if (result is ModuleLoadResult.Failure f)
        {
            var msg = string.Join("\n", f.Errors.Select(e => e.ToString()));
            Assert.Fail("Module failed to load:\n" + msg);
        }

        result.Should().BeOfType<ModuleLoadResult.Success>();
        var module = result.ModuleOrNull!;
        module.Manifest.Id.Should().Be("abandoned-mansion");
        module.Characters.Should().HaveCount(4);
        module.NpcCompanions.Should().HaveCount(3);
        module.Tiles.Should().HaveCount(19);
        module.Events.Should().HaveCount(13);
        module.ActionCards.Should().HaveCount(40);
        module.Equipment.Should().HaveCount(25);
        module.Endings.Should().HaveCount(4);
        module.Battles.Should().HaveCount(5);
    }

    [Fact]
    public void AbandonedMansion_AllCrossReferencesResolve()
    {
        var result = NewLoader().Load(TestPaths.AbandonedMansionFolder);
        result.Should().BeOfType<ModuleLoadResult.Success>();
        var m = result.ModuleOrNull!;

        m.Tiles.Should().ContainKey(m.Prologue.StartingTileId);
        foreach (var id in m.Prologue.ImportantTileIds)
            m.Tiles.Should().ContainKey(id);

        foreach (var ch in m.Characters.Values)
            foreach (var cardId in ch.StartingDeck)
                m.ActionCards.Should().ContainKey(cardId, $"character {ch.Id} references action card {cardId}");

        foreach (var b in m.Battles.Values)
            foreach (var eff in b.LootEffects)
                if (eff is GrantEquipmentEffect g)
                    m.Equipment.Should().ContainKey(g.Id, $"battle {b.Id} loot references equipment {g.Id}");

        foreach (var ev in m.Events.Values)
        {
            if (ev.Trigger is TileEnterTrigger tet)
                m.Tiles.Should().ContainKey(tet.TileId, $"event {ev.Id} triggers on tile {tet.TileId}");

            foreach (var outcome in new[] { ev.Outcomes.Success, ev.Outcomes.PartialSuccess, ev.Outcomes.Failure })
                foreach (var eff in outcome.Effects)
                {
                    switch (eff)
                    {
                        case GrantEquipmentEffect ge:
                            m.Equipment.Should().ContainKey(ge.Id);
                            break;
                        case TriggerBattleEffect tb:
                            m.Battles.Should().ContainKey(tb.BattleId);
                            break;
                    }
                }
        }
    }

    [Fact]
    public void AbandonedMansion_WinConditionsReferenceExistingFlags()
    {
        var result = NewLoader().Load(TestPaths.AbandonedMansionFolder);
        result.Should().BeOfType<ModuleLoadResult.Success>();
        var m = result.ModuleOrNull!;

        var declaredFlagKeys = new HashSet<string>();
        foreach (var ev in m.Events.Values)
            foreach (var outcome in new[] { ev.Outcomes.Success, ev.Outcomes.PartialSuccess, ev.Outcomes.Failure })
                foreach (var eff in outcome.Effects)
                    if (eff is SetFlagEffect sf)
                        declaredFlagKeys.Add(sf.Key);

        foreach (var b in m.Battles.Values)
            foreach (var eff in b.LootEffects)
                if (eff is SetFlagEffect sf)
                    declaredFlagKeys.Add(sf.Key);

        foreach (var t in m.Tiles.Values)
            foreach (var eff in t.OnEnter)
                if (eff is SetFlagEffect sf)
                    declaredFlagKeys.Add(sf.Key);

        foreach (var win in m.Prologue.WinConditions)
            if (win is FlagWinCondition fw)
                declaredFlagKeys.Should().Contain(fw.Key,
                    $"win condition flag '{fw.Key}' must be set by at least one event/battle/tile");
    }
}
