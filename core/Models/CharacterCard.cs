// CharacterCard — 角色卡（α 包裝型別）。
// 把 Character 包成卡片表示，給角色選擇畫面 / 角色圖鑑 / 結局回顧等情境使用。
// 卡片本體 (Id/Name/PortraitPath) 可獨立持久化、可進 deck / collection；
// 實際角色資料 (Stats/HpMax/StartingDeck 等) 仍由 Character 欄位指向 Character 取得。
namespace CardNarrative.Core.Models;

public sealed record CharacterCard(
    string Id,
    string Name,
    string Description,
    Character Character
)
{
    public string? PortraitPath { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}
