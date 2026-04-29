// S6: EventScheduler 已標 obsolete；測試自身保留以驗證舊 TurnLoop 路徑行為。
#pragma warning disable CS0618
using System.Text.Json;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class EventSchedulerTests
{
    [Fact]
    public void FindTriggeredOnEnterTile_Matches_Warehouse()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var sched = new EventScheduler();

        var ev = sched.FindTriggeredOnEnterTile("warehouse", state, module);

        ev.Should().NotBeNull();
        ev!.Id.Should().Be("warehouse-investigation");
    }

    [Fact]
    public void FindTriggeredOnEnterTile_SkipsConsumed()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.ConsumedEventIds.Add("warehouse-investigation");
        var sched = new EventScheduler();

        sched.FindTriggeredOnEnterTile("warehouse", state, module).Should().BeNull();
    }

    [Fact]
    public void FindTriggeredOnEventCheck_Matches_FlagTrigger()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        state.Flags["trigger_warehouse_guard"] = JsonDocument.Parse("true").RootElement;
        var sched = new EventScheduler();

        var ev = sched.FindTriggeredOnEventCheck(state, module);

        ev.Should().NotBeNull();
        ev!.Id.Should().Be("warehouse-guard");
    }

    [Fact]
    public void FindTriggeredOnEventCheck_NoFlag_ReturnsNull()
    {
        var module = ModuleFactory.Load();
        var state = ModuleFactory.NewState(module);
        var sched = new EventScheduler();

        sched.FindTriggeredOnEventCheck(state, module).Should().BeNull();
    }
}
