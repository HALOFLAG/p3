using CardNarrative.Core.Map;
using FluentAssertions;

namespace CardNarrative.Tests.Map;

/// <summary>
/// Phase 3 v1.12 Stage 1 — WorldMap.TagsCompatible 純邏輯驗證（規格書 §1.5 / §3.1.4）。
/// OR 邏輯：候選空 / 相鄰空 / 共享 1 個 → 相容；無交集才阻擋。
/// </summary>
public class WorldMapTagsCompatibleTests
{
    [Fact]
    public void TagsCompatible_BothEmpty_ReturnsTrue()
    {
        WorldMap.TagsCompatible(new List<string>(), new List<string>())
            .Should().BeTrue();
    }

    [Fact]
    public void TagsCompatible_CandidateEmpty_ReturnsTrue()
    {
        WorldMap.TagsCompatible(new List<string>(), new[] { "indoor" })
            .Should().BeTrue();
    }

    [Fact]
    public void TagsCompatible_NeighborEmpty_ReturnsTrue()
    {
        WorldMap.TagsCompatible(new[] { "indoor" }, new List<string>())
            .Should().BeTrue();
    }

    [Fact]
    public void TagsCompatible_SharedTag_ReturnsTrue()
    {
        WorldMap.TagsCompatible(
                new[] { "indoor", "mansion" },
                new[] { "outdoor", "mansion" })
            .Should().BeTrue();
    }

    [Fact]
    public void TagsCompatible_NoOverlap_ReturnsFalse()
    {
        WorldMap.TagsCompatible(
                new[] { "indoor" },
                new[] { "outdoor", "underground" })
            .Should().BeFalse();
    }
}
