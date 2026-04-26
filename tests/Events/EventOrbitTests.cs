// EventOrbitTests — ORBIT 任務板核心測試（規格 §1.6）。
// 涵蓋：7 槽 + 第 8 展開鈕、A→B→C 排序、Push/Remove/Promote、
// EventCheck 連鎖深度、deferred-to-next、TriggeredThisCheck 去重。
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using FluentAssertions;

namespace CardNarrative.Tests.Events;

public class EventOrbitTests
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

    private static EventInstance MakeInstance(string id, EventOrbitClass cls = EventOrbitClass.ClassC, bool isEnding = false)
        => new(MakeCard(id), initialClass: cls, isEnding: isEnding);

    [Fact]
    public void Push_DefaultsHiddenToClassC_AndRaisesChanged()
    {
        var orbit = new EventOrbit();
        int changes = 0;
        orbit.OrbitChanged += () => changes++;

        orbit.Push(new EventInstance(MakeCard("e1"))); // Hidden 預設

        orbit.Pending.Should().HaveCount(1);
        orbit.Pending[0].Class.Should().Be(EventOrbitClass.ClassC);
        changes.Should().Be(1);
    }

    [Fact]
    public void VisibleSlots_HasSevenSlotsAndExpandButtonAtEighth()
    {
        var orbit = new EventOrbit();
        for (int i = 0; i < 8; i++) orbit.Push(MakeInstance($"e{i}"));

        orbit.VisibleSlotsView().Should().HaveCount(EventOrbit.VisibleSlots).And.HaveCount(7);
        orbit.HasExpandButton.Should().BeTrue();
        orbit.ExpandedView().Should().HaveCount(8);
    }

    [Fact]
    public void VisibleSlots_BelowSeven_NoExpandButton()
    {
        var orbit = new EventOrbit();
        for (int i = 0; i < 5; i++) orbit.Push(MakeInstance($"e{i}"));

        orbit.HasExpandButton.Should().BeFalse();
        orbit.VisibleSlotsView().Should().HaveCount(5);
    }

    [Fact]
    public void VisibleSlots_OrderedAThenBThenC_FifoWithinClass()
    {
        var orbit = new EventOrbit();
        orbit.Push(MakeInstance("c1"));                        // C
        orbit.Push(MakeInstance("b1", EventOrbitClass.ClassB)); // B
        orbit.Push(MakeInstance("a1", EventOrbitClass.ClassA)); // A
        orbit.Push(MakeInstance("a2", EventOrbitClass.ClassA)); // A
        orbit.Push(MakeInstance("c2"));                        // C
        orbit.Push(MakeInstance("b2", EventOrbitClass.ClassB)); // B

        var ids = orbit.VisibleSlotsView().Select(i => i.Id).ToArray();
        ids.Should().Equal("a1", "a2", "b1", "b2", "c1", "c2");
    }

    [Fact]
    public void Promote_ChangesClassAndRaisesChanged()
    {
        var orbit = new EventOrbit();
        var inst = MakeInstance("x");
        orbit.Push(inst);
        int changes = 0;
        orbit.OrbitChanged += () => changes++;

        orbit.Promote(inst, EventOrbitClass.ClassA);

        inst.Class.Should().Be(EventOrbitClass.ClassA);
        changes.Should().Be(1);
    }

    [Fact]
    public void Promote_NoChangeWhenSameClass_DoesNotRaise()
    {
        var orbit = new EventOrbit();
        var inst = MakeInstance("x", EventOrbitClass.ClassB);
        orbit.Push(inst);
        int changes = 0;
        orbit.OrbitChanged += () => changes++;

        orbit.Promote(inst, EventOrbitClass.ClassB);

        changes.Should().Be(0);
    }

    [Fact]
    public void RemoveById_WorksAndRaisesChanged()
    {
        var orbit = new EventOrbit();
        orbit.Push(MakeInstance("a"));
        orbit.Push(MakeInstance("b"));
        int changes = 0;
        orbit.OrbitChanged += () => changes++;

        orbit.RemoveById("a").Should().BeTrue();
        orbit.Pending.Select(i => i.Id).Should().Equal("b");
        changes.Should().Be(1);

        orbit.RemoveById("missing").Should().BeFalse();
    }

    [Fact]
    public void BeginEventCheck_ClearsTriggeredAndChainDepth()
    {
        var orbit = new EventOrbit();
        orbit.MarkTriggered("e1");
        orbit.IncrementChainDepth();
        orbit.IncrementChainDepth();
        orbit.ChainDepth.Should().Be(2);
        orbit.IsTriggeredThisCheck("e1").Should().BeTrue();

        orbit.BeginEventCheck();

        orbit.ChainDepth.Should().Be(0);
        orbit.IsTriggeredThisCheck("e1").Should().BeFalse();
    }

    [Fact]
    public void BeginEventCheck_RestoresDeferredToFront()
    {
        var orbit = new EventOrbit();
        var existing = MakeInstance("existing", EventOrbitClass.ClassA);
        orbit.Push(existing);
        var deferred = MakeInstance("deferred", EventOrbitClass.ClassA);
        orbit.Push(deferred);
        orbit.DeferToNext(deferred);

        orbit.Pending.Should().NotContain(deferred);
        orbit.DeferredToNext.Should().Contain(deferred);

        orbit.BeginEventCheck();

        // 規格 §1.6：deferred 事件移到下回合 ORBIT 最前端
        orbit.Pending.Select(i => i.Id).Should().StartWith("deferred");
        orbit.DeferredToNext.Should().BeEmpty();
    }

    [Fact]
    public void IncrementChainDepth_AtMaxReturnsFalse()
    {
        var orbit = new EventOrbit();
        // MaxChainDepth = 5；前 4 次回 true，第 5 次（達上限）回 false
        for (int i = 1; i < EventOrbit.MaxChainDepth; i++)
            orbit.IncrementChainDepth().Should().BeTrue();
        orbit.IncrementChainDepth().Should().BeFalse();
    }
}
