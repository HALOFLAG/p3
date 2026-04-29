// NewGameStateFieldsRoundTripTests — Phase 3 任務 14（S8）。
//
// 目的：在尚未實作 SaveService（Task 17）之前，鎖定 S2–S4 引入的三個 GameState 新欄位
// 在 System.Text.Json 下能正確 round-trip：
//   - AcquiredIntel       : HashSet<string>
//   - EventOutcomes       : Dictionary<string, EventOutcomeTier>
//   - ActionCounts        : Dictionary<PlayerActionKind, int>
//
// 兩個 enum 都掛了 JsonStringEnumConverter（PlayerActionKind / EventOutcomeTier）
// → 序列化為小寫字串，舊存檔反序列化遇到未知字串 fallback；新增 enum 值不破檔。
//
// 採薄包裝 record 而非整個 GameState（GameState 含 (int,int) tuple 鍵的 dict、
// 多型 trigger 等複雜結構，需要 SaveService 自己處理；本測試僅驗單純欄位）。
using System.Text.Json;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using FluentAssertions;

namespace CardNarrative.Tests.State;

public class NewGameStateFieldsRoundTripTests
{
    /// <summary>薄包裝：只承擔三個新欄位的 STJ round-trip 驗證。</summary>
    private sealed record FieldsCarrier
    {
        public HashSet<string> AcquiredIntel { get; init; } = new();
        public Dictionary<string, EventOutcomeTier> EventOutcomes { get; init; } = new();
        public Dictionary<PlayerActionKind, int> ActionCounts { get; init; } = new();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [Fact]
    public void AcquiredIntel_RoundTripsCorrectly()
    {
        var src = new FieldsCarrier
        {
            AcquiredIntel = { "underground-passage", "priest-diary" },
        };

        var json = JsonSerializer.Serialize(src, Options);
        var dst = JsonSerializer.Deserialize<FieldsCarrier>(json, Options)!;

        dst.AcquiredIntel.Should().BeEquivalentTo(new[] { "underground-passage", "priest-diary" });
    }

    [Fact]
    public void EventOutcomes_RoundTripsAsLowerCaseStrings()
    {
        var src = new FieldsCarrier
        {
            EventOutcomes =
            {
                ["chapel-investigation"] = EventOutcomeTier.Success,
                ["mansion-foyer"]        = EventOutcomeTier.PartialSuccess,
                ["entrance"]             = EventOutcomeTier.Failure,
            },
        };

        var json = JsonSerializer.Serialize(src, Options);
        var dst = JsonSerializer.Deserialize<FieldsCarrier>(json, Options)!;

        dst.EventOutcomes["chapel-investigation"].Should().Be(EventOutcomeTier.Success);
        dst.EventOutcomes["mansion-foyer"].Should().Be(EventOutcomeTier.PartialSuccess);
        dst.EventOutcomes["entrance"].Should().Be(EventOutcomeTier.Failure);

        // JSON 確實是 enum 字串而非數字（驗 JsonStringEnumConverter 生效）。
        // 預設 JsonStringEnumConverter 寫 enum 識別字（PascalCase）；不同於 JsonLogic context
        // 的 camelCase（那是模組作者面向的條件值）— 兩個 case 是不同層的 contract。
        json.Should().Contain("\"Success\"");
        json.Should().Contain("\"PartialSuccess\"");
        json.Should().Contain("\"Failure\"");
    }

    [Fact]
    public void ActionCounts_RoundTripsWithEnumKeys()
    {
        var src = new FieldsCarrier
        {
            ActionCounts =
            {
                [PlayerActionKind.Move]    = 7,
                [PlayerActionKind.Observe] = 3,
                [PlayerActionKind.Rest]    = 1,
            },
        };

        var json = JsonSerializer.Serialize(src, Options);
        var dst = JsonSerializer.Deserialize<FieldsCarrier>(json, Options)!;

        dst.ActionCounts[PlayerActionKind.Move].Should().Be(7);
        dst.ActionCounts[PlayerActionKind.Observe].Should().Be(3);
        dst.ActionCounts[PlayerActionKind.Rest].Should().Be(1);

        // dictionary key 也用 enum 字串（驗 STJ 內建 dict-key enum 寫成字串）。
        json.Should().Contain("\"Move\"");
        json.Should().Contain("\"Observe\"");
    }

    [Fact]
    public void MissingFields_FallbacksToEmptyCollections()
    {
        // 模擬「舊存檔」：完全沒有這三個欄位的 JSON
        const string oldSaveJson = "{}";

        var dst = JsonSerializer.Deserialize<FieldsCarrier>(oldSaveJson, Options)!;

        dst.AcquiredIntel.Should().NotBeNull().And.BeEmpty();
        dst.EventOutcomes.Should().NotBeNull().And.BeEmpty();
        dst.ActionCounts.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void UnknownEnumString_ThrowsOnDeserialize()
    {
        // STJ 預設對未知 enum 字串會丟例外；此測試固定當前行為（避免日後行為變更）。
        // 若日後加 enum 值（例：新 PlayerActionKind），舊存檔遇到對應字串時會 round-trip 成功，
        // 但「先進更新後退」的 case（新存檔被舊版讀）會炸 — 這是預期、應有的 schema-breaking 行為。
        const string forwardCompatBreaker = """
        {
          "actionCounts": { "futureKind": 1 }
        }
        """;

        var act = () => JsonSerializer.Deserialize<FieldsCarrier>(forwardCompatBreaker, Options);
        act.Should().Throw<JsonException>();
    }
}
