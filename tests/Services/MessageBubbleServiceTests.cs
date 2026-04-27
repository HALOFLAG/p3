// 任務 12 Stage 1 — 訊息氣泡 service 後端核心測試（規格書 §1.9 指定 4 + 邊界 2 = 6 測試）。
// 規格指定的 4 測試名（用 _ 命名 fact 對應 §1.9 驗證清單）：
//   - Push_OrderedByTimestamp_NewestFirst
//   - OnTurnEnd_ClearsAllBubbles
//   - GetVisible_FiltersByPhase
//   - RequestNavigation_OrbitSource_ReturnsSlotTarget
// 邊界補強：
//   - Push_BeyondCapacity_DropsOldest（容量 20 環形）
//   - RequestNavigation_CompanionSource_ReturnsCompanionTarget
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.Services;

public class MessageBubbleServiceTests
{
    private static DateTime T(int s) => new DateTime(2026, 1, 1, 0, 0, s, DateTimeKind.Utc);

    [Fact]
    public void Push_OrderedByTimestamp_NewestFirst()
    {
        // §1.9 規格：GetVisible 回傳清單最新在上（玩家點開 popup 看到最新訊息在頂端）
        var svc = new MessageBubbleService();
        svc.Push("first", MessageBubbleSource.OrbitSlot, "slot:0", T(0), isImportant: false);
        svc.Push("second", MessageBubbleSource.SystemHint, "tip-1", T(1), isImportant: false);
        svc.Push("third", MessageBubbleSource.CompanionCard, "old-priest", T(2), isImportant: false);

        var visible = svc.GetVisible(MessageBubbleVisibilityPhase.Action);

        visible.Select(b => b.Text).Should().ContainInOrder("third", "second", "first");
    }

    [Fact]
    public void OnTurnEnd_ClearsAllBubbles()
    {
        // §1.9 規格：TurnEnd 清空當回合訊息；下回合重評估。
        var svc = new MessageBubbleService();
        svc.Push("a", MessageBubbleSource.OrbitSlot, "slot:0", T(0), isImportant: true);
        svc.Push("b", MessageBubbleSource.SystemHint, "tip", T(1), isImportant: false);
        svc.Bubbles.Should().HaveCount(2);

        int clearedCount = 0;
        svc.MessageCleared += (_, _) => clearedCount++;
        svc.OnTurnEnd();

        svc.Bubbles.Should().BeEmpty();
        clearedCount.Should().Be(1);
    }

    [Fact]
    public void GetVisible_FiltersByPhase()
    {
        // §1.9 規格：MapExpand / Action / EventCheck 顯示；TurnEnd 隱藏（回空 list）。
        var svc = new MessageBubbleService();
        svc.Push("hello", MessageBubbleSource.OrbitSlot, "slot:0", T(0), isImportant: false);

        svc.GetVisible(MessageBubbleVisibilityPhase.MapExpand).Should().HaveCount(1);
        svc.GetVisible(MessageBubbleVisibilityPhase.Action).Should().HaveCount(1);
        svc.GetVisible(MessageBubbleVisibilityPhase.EventCheck).Should().HaveCount(1);
        svc.GetVisible(MessageBubbleVisibilityPhase.TurnEnd).Should().BeEmpty();
    }

    [Fact]
    public void RequestNavigation_OrbitSource_ReturnsSlotTarget()
    {
        // §1.9 規格：點 OrbitSlot 訊息 → NavigationTarget(Orbit, slotIndex)
        // SourceId 慣例為 "slot:N"，service 解析出 SlotIndex 給 UI 滾動高亮。
        var svc = new MessageBubbleService();
        var bubble = svc.Push("結算 village-inquiry → 成功",
            MessageBubbleSource.OrbitSlot, "slot:3", T(0), isImportant: true);

        var target = svc.RequestNavigation(bubble);

        target.Kind.Should().Be(NavigationKind.Orbit);
        target.SlotIndex.Should().Be(3);
        target.TargetId.Should().Be("slot:3");
    }

    [Fact]
    public void Push_BeyondCapacity_DropsOldest()
    {
        // 邊界：容量 MaxPerTurn = 20，超過時丟最舊（環形）；TurnEnd 清空，但回合內可能爆量。
        var svc = new MessageBubbleService();
        for (int i = 0; i < MessageBubbleService.MaxPerTurn + 5; i++)
            svc.Push($"msg-{i}", MessageBubbleSource.SystemHint, $"tip-{i}", T(i), isImportant: false);

        svc.Bubbles.Should().HaveCount(MessageBubbleService.MaxPerTurn);
        // 最舊 5 筆（msg-0..msg-4）已被丟，第一筆應該是 msg-5
        svc.Bubbles.First().Text.Should().Be("msg-5");
        svc.Bubbles.Last().Text.Should().Be($"msg-{MessageBubbleService.MaxPerTurn + 4}");
    }

    [Fact]
    public void RequestNavigation_CompanionSource_ReturnsCompanionTarget()
    {
        // 邊界：點 CompanionCard 訊息 → NavigationTarget(Companion, companionId, null)
        // LeftPanel 訂閱後依 companion id 反查同伴卡 view 並 pulse 高亮。
        var svc = new MessageBubbleService();
        var bubble = svc.Push("老牧師：「這個房間有點怪…」",
            MessageBubbleSource.CompanionCard, "old-priest", T(0), isImportant: false);

        var target = svc.RequestNavigation(bubble);

        target.Kind.Should().Be(NavigationKind.Companion);
        target.TargetId.Should().Be("old-priest");
        target.SlotIndex.Should().BeNull();
    }
}
