// EffectHandler — 將 EffectBase 多型效果逐一套用到 GameState。
// 支援：SetFlag、GrantResource、ClimaxTnIncrease、TriggerBattle、
// TileProgress（呼叫 TileProgressService）、GrantEquipment（auto-equip / 入背包 / pending 三段 fallback）、
// RollCheck / DrawEncounter 由 EventResolver 處理。
using System.Text.Json;
using CardNarrative.Core.Cards;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public interface IEffectHandler
{
    void Apply(EffectBase effect, GameState state, Module? module = null);
}

public sealed class EffectHandler : IEffectHandler
{
    public void Apply(EffectBase effect, GameState state, Module? module = null)
    {
        switch (effect)
        {
            case SetFlagEffect f:
                state.Flags[f.Key] = f.Value;
                break;
            case GrantResourceEffect g:
                state.Resources[g.Key] = state.Resources.GetValueOrDefault(g.Key) + g.Amount;
                break;
            case ClimaxTnIncreaseEffect c:
                state.ClimaxTnBonus += c.Amount;
                break;
            case TriggerBattleEffect b:
                state.PendingBattleId = b.BattleId;
                break;
            case TileProgressEffect t:
                var pos = state.CurrentPlayer.Position;
                if (module is not null)
                {
                    TileProgressService.Advance(state, module, pos, t.Delta, ProgressReason.EventOutcome);
                }
                else if (state.TileMap.TryGetValue((pos.X, pos.Y), out var placed))
                {
                    // Fallback when module isn't supplied (older callers); skip Important-ceiling check.
                    int target = Math.Clamp((int)placed.Level + t.Delta, -2, 2);
                    placed.Level = (ExplorationLevel)target;
                }
                break;
            case GrantEquipmentEffect ge:
                ApplyGrantEquipment(ge, state, module);
                break;
            case TransformTileEffect tt:
                ApplyTransformTile(tt, state, module);
                break;
            case TransformTilesByTagEffect tbt:
                ApplyTransformTilesByTag(tbt, state, module);
                break;
            case RollCheckEffect:
            case DrawEncounterEffect:
                // deferred to M10 — no-op
                break;
        }
    }

    // B2 · 轉變單格 tile。
    // Position 省略 → 取 CurrentPlayer.Position（TileEnter context 典型用法）。
    // Level / 採集 / interaction usage / action-card-once 旗標全部重置（視為全新 tile）。
    private static void ApplyTransformTile(TransformTileEffect t, GameState state, Module? module)
    {
        if (module is null) return;
        if (!module.Tiles.ContainsKey(t.NewTileId)) return;

        var pos = (t.X is int x && t.Y is int y)
            ? (x, y)
            : (state.CurrentPlayer.Position.X, state.CurrentPlayer.Position.Y);

        if (!state.TileMap.TryGetValue(pos, out var placed)) return;

        ResetPlacedTileForTransform(placed, t.NewTileId, state.CurrentBigRound);

        if (!t.PreservePlayerPosition)
            TryPushPlayerOff(state, pos);
    }

    // B2 · 批次轉變：Tags 全部包含才 match（AND）。玩家位置永遠保留。
    private static void ApplyTransformTilesByTag(TransformTilesByTagEffect batch, GameState state, Module? module)
    {
        if (module is null) return;
        if (!module.Tiles.ContainsKey(batch.NewTileId)) return;
        if (batch.Tags is null || batch.Tags.Count == 0) return;

        foreach (var placed in state.TileMap.Values)
        {
            if (!module.Tiles.TryGetValue(placed.TileId, out var tile)) continue;
            var tags = tile.Tags;
            bool allMatch = true;
            for (int i = 0; i < batch.Tags.Count; i++)
            {
                if (!tags.Contains(batch.Tags[i])) { allMatch = false; break; }
            }
            if (!allMatch) continue;

            ResetPlacedTileForTransform(placed, batch.NewTileId, state.CurrentBigRound);
        }
    }

    private static void ResetPlacedTileForTransform(PlacedTile placed, string newTileId, int currentBigRound)
    {
        placed.TileId = newTileId;
        placed.Level = ExplorationLevel.Unknown;
        placed.ProgressGainedThisBigRound = 0;
        placed.LastProgressBigRound = currentBigRound;
        placed.ActionCardProgressUsedThisVisit = false;
        placed.LastVisitBigRound = 0;
        placed.ResourcesCollectedByPlayer.Clear();
        placed.InteractionsUsedOncePerGame.Clear();
        placed.InteractionsUsedThisVisit.Clear();
        placed.InteractionsUsedThisBigRound.Clear();
    }

    // 玩家不固定位置：若某玩家站在被變化的 tile 上，嘗試推到 4 鄰且已放置 tile；
    // 找不到就留在原位（rare edge；通常 TileEnter 觸發時鄰居仍存在）。
    private static void TryPushPlayerOff(GameState state, (int X, int Y) pos)
    {
        foreach (var player in state.Players)
        {
            if (player.Position.X != pos.X || player.Position.Y != pos.Y) continue;
            var neighbors = new (int X, int Y)[]
            {
                (pos.X - 1, pos.Y), (pos.X + 1, pos.Y),
                (pos.X, pos.Y - 1), (pos.X, pos.Y + 1),
            };
            foreach (var n in neighbors)
            {
                if (state.TileMap.ContainsKey(n))
                {
                    player.Position = new Position(n.X, n.Y);
                    break;
                }
            }
        }
    }

    private static void ApplyGrantEquipment(GrantEquipmentEffect ge, GameState state, Module? module)
    {
        var player = state.CurrentPlayer;

        // 規格書 §3.4.3 「獲得即入背包」：以背包為主、auto-equip 為便利路徑、PendingEquipmentGrants 為最後 fallback。
        // 三段 fallback：
        //   1. 主槽空 + module 已知裝備 → auto-equip 至主槽（保留 GrantEquipmentEffectTests 的便利語意）
        //   2. 主槽被占 / 角色卡槽 / module 未知 → 嘗試入背包
        //   3. 背包也滿 → PendingEquipmentGrants（玩家手動處理）

        if (module is not null && module.Equipment.TryGetValue(ge.Id, out var eq))
        {
            var target = eq.Slot;
            player.Equipment.TryGetValue(target, out var existing);
            if (target != player.CharacterCardSlot && existing is null)
            {
                player.Equipment[target] = ge.Id;
                return;
            }
        }

        // 入背包（規格書 §3.4.3）
        if (player.Backpack.Count < EquipmentManager.BackpackMax)
        {
            player.Backpack.Insert(0, ge.Id); // 進背包頂端，與 EquipmentManager.AddToBackpack 行為一致
            return;
        }

        // 背包也滿，由玩家手動處理
        player.PendingEquipmentGrants.Add(ge.Id);
    }
}
