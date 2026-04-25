// CompanionCommandService — B9 · 玩家主動指揮同伴系統。
// 消耗同伴自身的 AP（固定 1）執行 4 種戰術命令：Scout / Search / Guard / Support。
// 與 A2 同伴自動行動互補：A2 自動；B9 玩家主動。
// 冷卻：CommandsUsedThisBigRound（大回合）+ CommandsUsedThisVisit（每 tile 訪問）。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public sealed record CompanionCommandResult(
    bool Success,
    string Message,
    IReadOnlyList<string>? PreviewedTiles = null
);

public sealed class CompanionCommandService
{
    private readonly Module _module;
    private readonly GameState _state;
    private readonly ITurnLogSink? _log;

    public CompanionCommandService(Module module, GameState state, ITurnLogSink? log = null)
    {
        _module = module;
        _state = state;
        _log = log;
    }

    /// <summary>判斷特定同伴能否下該命令（UI CanExecute）。</summary>
    public bool CanExecute(string companionId, CompanionCommandKind cmd)
    {
        var cs = _state.Companions.FirstOrDefault(c => c.CompanionId == companionId);
        if (cs is null || cs.Hp <= 0) return false;
        // 同伴 AP 由 NpcCompanions 模組定義；>=1 才能下命令
        if (!_module.NpcCompanions.TryGetValue(companionId, out var npc)) return false;
        if (npc.ActionPoints < 1) return false;
        if (_state.Phase != TurnPhase.Action
#pragma warning disable CS0618
            && _state.Phase != TurnPhase.Move
#pragma warning restore CS0618
        ) return false;

        switch (cmd)
        {
            case CompanionCommandKind.Scout:
                return true;  // 無冷卻
            case CompanionCommandKind.Search:
                var player = _state.Players.FirstOrDefault(p => p.BoundCompanionId == companionId);
                if (player is null) return false;
                var tileKey = TileVisitKey(player.Position);
                return !HasVisitFlag(cs, tileKey, cmd);
            case CompanionCommandKind.Guard:
                return !cs.HasGuardPending
                       && !cs.CommandsUsedThisBigRound.Contains(cmd);
            case CompanionCommandKind.Support:
                return !cs.CommandsUsedThisBigRound.Contains(cmd);
            default: return false;
        }
    }

    public CompanionCommandResult Execute(string companionId, CompanionCommandKind cmd)
    {
        if (!CanExecute(companionId, cmd))
            return new CompanionCommandResult(false, $"命令「{cmd}」無法執行（AP 不足、冷卻中、或階段不符）");

        var cs = _state.Companions.First(c => c.CompanionId == companionId);
        var npc = _module.NpcCompanions[companionId];
        var player = _state.Players.First(p => p.BoundCompanionId == companionId);

        CompanionCommandResult result;
        switch (cmd)
        {
            case CompanionCommandKind.Scout: result = DoScout(cs, npc, player); break;
            case CompanionCommandKind.Search: result = DoSearch(cs, npc, player); break;
            case CompanionCommandKind.Guard: result = DoGuard(cs, npc, player); break;
            case CompanionCommandKind.Support: result = DoSupport(cs, npc, player); break;
            default: return new CompanionCommandResult(false, "未知命令");
        }

        if (result.Success)
        {
            // 依命令類型記入對應冷卻集合
            if (cmd == CompanionCommandKind.Search)
                MarkVisit(cs, TileVisitKey(player.Position), cmd);
            else if (cmd == CompanionCommandKind.Guard || cmd == CompanionCommandKind.Support)
                cs.CommandsUsedThisBigRound.Add(cmd);

            _log?.Append($"指揮 {npc.Name} · {DisplayName(cmd)}：{result.Message}", TurnLogKind.Neutral);
        }
        return result;
    }

    private CompanionCommandResult DoScout(CompanionState cs, NpcCompanion npc, PlayerState player)
    {
        // 揭露 TileDeck 頂 2 張
        var peek = _state.TileDeck.Take(2)
            .Select(id => _module.Tiles.TryGetValue(id, out var t) ? t.Name : id)
            .ToList();
        var text = peek.Count > 0 ? string.Join(" → ", peek) : "（牌堆空）";
        return new CompanionCommandResult(true, $"預覽地塊牌堆頂：{text}", peek);
    }

    private CompanionCommandResult DoSearch(CompanionState cs, NpcCompanion npc, PlayerState player)
    {
        if (!_state.TileMap.TryGetValue((player.Position.X, player.Position.Y), out var placed))
            return new CompanionCommandResult(false, "目前不在任何地塊上");
        if (!_module.Tiles.TryGetValue(placed.TileId, out var tile))
            return new CompanionCommandResult(false, "地塊資料缺失");
        // 找第一個尚未被該玩家採集的 resource
        var playerIdx = _state.Players.IndexOf(player);
        if (!placed.ResourcesCollectedByPlayer.TryGetValue(playerIdx, out var collected))
        {
            collected = new HashSet<string>();
            placed.ResourcesCollectedByPlayer[playerIdx] = collected;
        }
        var res = tile.Resources.FirstOrDefault(r => !collected.Contains(r.Name));
        if (res is null)
            return new CompanionCommandResult(false, "此地塊無可採集資源");
        collected.Add(res.Name);
        _state.Resources[res.Name] = _state.Resources.GetValueOrDefault(res.Name) + res.PerVisit;
        return new CompanionCommandResult(true, $"同伴幫忙採集 ✦{res.Name}×{res.PerVisit}");
    }

    private CompanionCommandResult DoGuard(CompanionState cs, NpcCompanion npc, PlayerState player)
    {
        cs.HasGuardPending = true;
        return new CompanionCommandResult(true, "已佈下守衛，下次 tileEnter 事件將被阻擋一次");
    }

    private CompanionCommandResult DoSupport(CompanionState cs, NpcCompanion npc, PlayerState player)
    {
        _state.PendingRollBonuses.Add(new PendingRollBonus
        {
            Amount = 2,
            Duration = SpecialtyEffectDuration.NextRoll,
            SourceCompanionId = npc.Id,
        });
        return new CompanionCommandResult(true, "下次擲骰 +2");
    }

    private static string TileVisitKey(Position pos) => $"{pos.X},{pos.Y}";

    private static bool HasVisitFlag(CompanionState cs, string key, CompanionCommandKind cmd)
        => cs.CommandsUsedThisVisit.TryGetValue(key, out var set) && set.Contains(cmd);

    private static void MarkVisit(CompanionState cs, string key, CompanionCommandKind cmd)
    {
        if (!cs.CommandsUsedThisVisit.TryGetValue(key, out var set))
        {
            set = new HashSet<CompanionCommandKind>();
            cs.CommandsUsedThisVisit[key] = set;
        }
        set.Add(cmd);
    }

    /// <summary>B9 · 大回合邊界呼叫：重置 CommandsUsedThisBigRound、未消耗的 Guard 也清掉。</summary>
    public static void ResetPerBigRound(GameState state)
    {
        foreach (var cs in state.Companions)
        {
            cs.CommandsUsedThisBigRound.Clear();
            // Guard buff 跨大回合未消耗則過期
            cs.HasGuardPending = false;
        }
    }

    /// <summary>B9 · 玩家移動到新 tile 時呼叫：清空舊 tile 的 per-visit 記錄（此處保留所有 tile key；未使用的自然不影響）。</summary>
    /// <remarks>簡化：per-visit 只影響來訪當時；玩家回到老 tile 重新算作新 visit，所以乾脆 Move 時清掉所有舊 visit 記錄。</remarks>
    public static void ResetPerVisit(GameState state)
    {
        foreach (var cs in state.Companions)
            cs.CommandsUsedThisVisit.Clear();
    }

    private static string DisplayName(CompanionCommandKind cmd) => cmd switch
    {
        CompanionCommandKind.Scout => "偵察",
        CompanionCommandKind.Search => "搜尋",
        CompanionCommandKind.Guard => "守衛",
        CompanionCommandKind.Support => "支援",
        _ => cmd.ToString(),
    };
}
