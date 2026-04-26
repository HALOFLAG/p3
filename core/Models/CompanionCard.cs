// CompanionCard — 同伴卡（α 包裝型別）。
// 把 NpcCompanion 包成卡片表示，給卡牌系統 UI / 招募流程 / 圖鑑等情境使用。
// 卡片本體 (Id/Name/PortraitPath) 可獨立持久化、可進 deck / hand / collection；
// 實際同伴資料 (Stats/Hp/Specialty 等) 仍由 Companion 欄位指向 NpcCompanion 取得。
namespace CardNarrative.Core.Models;

public sealed record CompanionCard(
    string Id,
    string Name,
    string Description,
    NpcCompanion Companion
)
{
    public string? PortraitPath { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
