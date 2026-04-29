// Intel — 玩家可取得的「情報 / 知識」（Phase 3 任務 14 / S4）。
//
// 概念：
//   - 不是裝備，而是「遊戲中經對話 / 事件 / 探索解鎖的隱藏知識」。
//   - 取得方式：grantIntel effect（事件 outcome 觸發）。
//   - 用途：
//     a) 作為事件 RevealCondition（A5）：{"var":"hasIntel.<id>"} 只有取得後事件才揭露
//     b) 解鎖某些 tag 的 tile 可被放置（UnlocksTags）：例如取得「教堂下隱藏地下道」情報
//        後，含 "underground" tag 的 tile 才能進入可放置候選格清單
//
// JSON 範例（intel.json，模組根目錄選用檔）：
//   [
//     {
//       "id": "underground-passage",
//       "name": "教堂下的地下道",
//       "description": "你從村民閒談中得知教堂底下有一條通往廢屋的地下通道。",
//       "unlocksTags": ["underground"]
//     }
//   ]
namespace CardNarrative.Core.Models;

public sealed record Intel(
    string Id,
    string Name,
    string Description
)
{
    /// <summary>
    /// 取得此情報後解鎖的 tile tag 清單。
    /// TileDeckService 在判斷可放置候選格時：含 tag T 的 tile 若存在某 Intel 的 UnlocksTags 含 T、
    /// 且玩家未取得任一含 T 的 Intel → 從候選池剔除。
    /// 缺省為空（純條件用情報，不影響 tile 放置）。
    /// </summary>
    public IReadOnlyList<string> UnlocksTags { get; init; } = Array.Empty<string>();
}
