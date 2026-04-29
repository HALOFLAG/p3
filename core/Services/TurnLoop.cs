// TurnLoop — 大回合主流程驅動（Draw → Move → Action → EventCheck → MapExpand → TurnEnd）。
// Draw / EventCheck / TurnEnd 為「自動」階段，除非觸發事件或抵達使用者輸入階段，
// 否則 Advance / PlaceTile / SkipPlacement / ResolvePendingEvent 會自動串接通過。
// 使用者輸入階段：Move、Action、MapExpand（牌堆非空）。
// 主要 API：Advance（推進階段）、PlayCard（行動卡結算）、Move/PlaceTile/SkipPlacement、
// EnqueueEvent/ResolvePendingEvent、CollectResource、InvestCluesForProgress、EndPlayerTurn。
// 與 EffectHandler / EventScheduler / TileProgressService / EquipmentService 協作；
// 管理手牌循環（抽牌 + 棄牌堆重洗、事件起始補滿、事件間補 2 張上限）。
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

// TurnLoop still references TurnPhase.Move so legacy saves flow through the
// same code path as Action without a special-case branch; see DoDraw/Advance.
// Phase 3 任務 14（S6）起 EventScheduler 已標 obsolete；TurnLoop 暫時保留依賴
// 直到全測遷移至 EventBroker；S7+ 起再評估是否拆除此 fallback 路徑。
#pragma warning disable CS0618 // TurnPhase.Move + EventScheduler are obsolete

namespace CardNarrative.Core.Services;

public sealed record MoveResult(Position From, Position To, bool UsedFamiliarFree, bool TriggeredOnEnter);

public sealed class TurnLoop
{
    public const int HandSizeLimit = 5;
    public const int ActionPointsPerTurn = 3;

    private readonly Module _module;
    private readonly IDiceService _dice;
    private readonly EventScheduler? _scheduler;
    private readonly EventOrbitResolver? _orbit; // Task 9 · ORBIT 軌道（可選；null 走舊單發 scheduler 路徑）
    private readonly ITurnLogSink? _log;
    private readonly IEffectHandler _effectHandler;
    // A2 · 同伴主動行動
    private readonly NpcAi _npcAi = new();
    // A3 · Specialty 規則引擎
    private readonly CompanionAbilityHandler _companionAbilities;

    public GameState State { get; }
    public RollResult? LastRoll { get; private set; }
    public EventCard? PendingEvent { get; private set; }

    /// <summary>Task 9 · 由 ORBIT EventCheck 觸發但本回合無法立即結算的事件，已排入 PendingEventQueue 後仍可參考。</summary>
    public EventOrbitResolver? Orbit => _orbit;

    public TurnLoop(GameState state, IDiceService dice, Module module,
        EventScheduler? scheduler = null, ITurnLogSink? log = null,
        IEffectHandler? effectHandler = null,
        CompanionAbilityHandler? companionAbilities = null,
        EventOrbitResolver? orbit = null)
    {
        State = state;
        _dice = dice;
        _module = module;
        _scheduler = scheduler;
        _orbit = orbit;
        _log = log;
        _effectHandler = effectHandler ?? new EffectHandler();
        _companionAbilities = companionAbilities ?? new CompanionAbilityHandler(log);
    }

    public void Advance()
    {
        if (PendingEvent is not null)
            throw new InvalidOperationException("cannot advance while an event is pending resolution");

        // User-input transitions that require an event check on leaving.
        // Move+Action are merged into a single "Action" user-input phase where
        // the player can move, play cards, collect resources, invest clues, and
        // advance freely. `TurnPhase.Move` is retained only for legacy saves
        // (migrated to Action on load) and is handled identically here.
        switch (State.Phase)
        {
            case TurnPhase.Move:
            case TurnPhase.Action:
                // Mark EventCheck before calling TryTriggerOnEventCheck so a
                // triggered pending event carries the EventCheck phase tag;
                // ResolvePendingEvent uses this tag to auto-chain to MapExpand.
                State.Phase = TurnPhase.EventCheck;
                if (TryTriggerOnEventCheck()) return;
                break; // fall through into auto-chain
        }

        AdvanceAutoPhases();
    }

    /// <summary>
    /// Auto-advances through Draw / EventCheck / TurnEnd and auto-skips an empty MapExpand.
    /// Stops at user-input phases (Move, Action, non-empty MapExpand), at a pending event,
    /// or at game over (phase left at Draw).
    /// </summary>
    private void AdvanceAutoPhases()
    {
        while (PendingEvent is null)
        {
            switch (State.Phase)
            {
                case TurnPhase.Draw:
                    State.Phase = DoDraw(); // → Move
                    return;
                case TurnPhase.EventCheck:
                    State.Phase = TurnPhase.MapExpand;
                    continue;
                case TurnPhase.MapExpand:
                    if (State.TileDeck.Count == 0)
                    {
                        State.Phase = TurnPhase.TurnEnd;
                        continue;
                    }
                    return; // user places or skips
                case TurnPhase.TurnEnd:
                    State.Phase = BeginNextTurnAndReturnPhase(); // → Draw
                    if (IsGameOver(out _)) return;
                    continue;
                default:
                    return; // Move / Action: user input
            }
        }
    }

    // ─── MapExpand API ────────────────────────────────────────────────

    public IReadOnlyList<Position> GetValidPlacementCells()
    {
        var next = TileDeckService.Peek(State);
        if (next is null) return Array.Empty<Position>();
        return TileDeckService.GetValidPlacementCells(State, _module, next);
    }

    public string? PeekNextTile() => TileDeckService.Peek(State);

    public void PlaceTile(string tileId, int x, int y)
    {
        if (State.Phase != TurnPhase.MapExpand)
            throw new InvalidOperationException($"cannot place tile outside MapExpand phase (current: {State.Phase})");
        TileDeckService.Place(State, _module, tileId, new Position(x, y));
        var name = _module.Tiles.TryGetValue(tileId, out var tileDef) ? tileDef.Name : tileId;
        _log?.Append($"放置地塊「{name}」於 ({x}, {y})。", TurnLogKind.Neutral);
        State.Phase = TurnPhase.TurnEnd;
        AdvanceAutoPhases(); // chain TurnEnd → next turn's Move
    }

    public void SkipPlacement()
    {
        if (State.Phase != TurnPhase.MapExpand)
            throw new InvalidOperationException($"cannot skip placement outside MapExpand phase (current: {State.Phase})");

        // Skip cascade: if the next tile has existing copies already placed
        // (i.e. would be a 2nd/3rd corridor segment) AND the player skips the
        // first segment, also skip the consecutive same-id copies in the deck
        // so the following turn doesn't try to place an orphan corridor piece.
        var nextId = TileDeckService.Peek(State);
        if (nextId is not null && !TileDeckService.HasPlacedCopy(State, nextId))
        {
            int skipped = 0;
            while (State.TileDeck.Count > 0 && State.TileDeck[0] == nextId)
            {
                State.TileDeck.RemoveAt(0);
                skipped++;
            }
            if (skipped > 1)
                _log?.Append($"略過地塊放置（連帶跳過 {skipped - 1} 份同 id 延續段）。", TurnLogKind.Neutral);
            else
                _log?.Append("略過地塊放置。", TurnLogKind.Neutral);
        }
        else
        {
            _log?.Append("略過地塊放置。", TurnLogKind.Neutral);
        }

        State.Phase = TurnPhase.TurnEnd;
        AdvanceAutoPhases();
    }

    // ─── Move API ─────────────────────────────────────────────────────

    public IReadOnlyList<Position> GetMoveTargets()
    {
        var pos = State.CurrentPlayer.Position;
        var result = new List<Position>();
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            var cell = (pos.X + dx, pos.Y + dy);
            if (!State.TileMap.ContainsKey(cell)) continue;
            // §13-2 Plan A 硬限制：跨區域 tags 必須共享（或任一側為中立 / 空 tags）。
            if (!TileDeckService.IsTagMoveAllowed(State, _module, pos, new Position(cell.Item1, cell.Item2)))
                continue;
            result.Add(new Position(cell.Item1, cell.Item2));
        }
        return result;
    }

    public MoveResult Move(Position target)
    {
        // Move+Action are merged — both phases accept move input (Move retained for legacy saves).
        if (State.Phase != TurnPhase.Move && State.Phase != TurnPhase.Action)
            throw new InvalidOperationException($"cannot move outside Action phase (current: {State.Phase})");
        var player = State.CurrentPlayer;
        var from = player.Position;
        if (Math.Abs(target.X - from.X) + Math.Abs(target.Y - from.Y) != 1)
            throw new InvalidOperationException("target must be 4-connected to current position");
        if (!State.TileMap.TryGetValue((target.X, target.Y), out var placedTarget))
            throw new InvalidOperationException("target cell has no placed tile");
        // §13-2 Plan A 硬限制：跨區域 tags 須共享（或任一側為中立空 tags）。
        if (!TileDeckService.IsTagMoveAllowed(State, _module, from, target))
            throw new InvalidOperationException(
                $"cannot move from ({from.X},{from.Y}) to ({target.X},{target.Y}): tags do not share a region");

        // Determine move cost:
        //   1st move of the turn = free
        //   2nd move requires AP >= 1 and zeroes out all AP
        //   Moving into a Familiar+ tile is free once per turn (doesn't count as a move)
        bool familiarFree = (int)placedTarget.Level >= (int)ExplorationLevel.Familiar
                            && !State.UsedFamiliarFreeMoveThisTurn;

        if (!familiarFree)
        {
            if (player.MovesThisTurn == 0)
            {
                player.MovesThisTurn = 1;
            }
            else
            {
                if (player.ActionPoints < 1)
                    throw new InvalidOperationException("second move requires at least 1 action point");
                player.ActionPoints = 0;
                player.MovesThisTurn = 2;
            }
        }
        else
        {
            State.UsedFamiliarFreeMoveThisTurn = true;
        }

        player.Position = target;
        // B9 · 新 tile → 清同伴 per-visit 命令記錄
        CompanionCommandService.ResetPerVisit(State);
        _log?.Append($"移動至 ({target.X}, {target.Y})。", TurnLogKind.Neutral);

        bool triggered = ApplyTileEntry(placedTarget);
        return new MoveResult(from, target, familiarFree, triggered);
    }

    /// <summary>
    /// Shared tile-entry logic used by both Move and the initial spawn:
    ///   1. per-visit reset (exploration promote, progress flags, resource set)
    ///   2. apply <c>tile.OnEnter</c> effects through the effect handler
    ///   3. fire any matching <c>tileEnter</c> event card (returns true if one became pending)
    /// </summary>
    private bool ApplyTileEntry(PlacedTile placedTarget)
    {
        TileProgressService.PromoteOnFirstEnter(placedTarget);
        placedTarget.ActionCardProgressUsedThisVisit = false;
        if (placedTarget.ResourcesCollectedByPlayer.TryGetValue(State.CurrentPlayerIndex, out var set))
            set.Clear();
        // A6 · 每次訪問時重置 OncePerVisit interactions
        if (placedTarget.InteractionsUsedThisVisit.TryGetValue(State.CurrentPlayerIndex, out var visitSet))
            visitSet.Clear();
        placedTarget.LastVisitBigRound = State.CurrentBigRound;

        // A3 · OnEnterTile 同伴 Specialty（例：重要地塊給 +1 線索碎片）
        _companionAbilities.Fire(SpecialtyTrigger.OnEnterTile, State, _module);

        // Apply tile.OnEnter effects (setFlag / grantResource / climaxTnIncrease / ...).
        // rollCheck / drawEncounter are currently no-ops in EffectHandler (deferred).
        if (_module.Tiles.TryGetValue(placedTarget.TileId, out var tileDef))
        {
            foreach (var e in tileDef.OnEnter)
                _effectHandler.Apply(e, State, _module);
        }

        return TryTriggerOnEnterTile();
    }

    /// <summary>
    /// Apply the starting tile's onEnter logic exactly once when the game begins.
    /// Called by GameSession after initial Draw so the player has a hand when an
    /// event pops. Idempotent guard: will not fire if the starting tile has
    /// already been marked as visited this big round.
    /// </summary>
    public bool TriggerStartingTileEntry()
    {
        if (State.StartingTileInitialized) return false;
        var pos = State.CurrentPlayer.Position;
        if (!State.TileMap.TryGetValue((pos.X, pos.Y), out var placed)) return false;
        State.StartingTileInitialized = true;
        return ApplyTileEntry(placed);
    }

    // ─── Resource / Clue API ──────────────────────────────────────────

    public bool CollectResource(string resourceKey)
    {
        var player = State.CurrentPlayer;
        if (!State.TileMap.TryGetValue((player.Position.X, player.Position.Y), out var placed)) return false;
        if (!_module.Tiles.TryGetValue(placed.TileId, out var tile)) return false;

        var resource = tile.Resources.FirstOrDefault(r => r.Key == resourceKey);
        if (resource is null) return false;

        if (!placed.ResourcesCollectedByPlayer.TryGetValue(State.CurrentPlayerIndex, out var collected))
        {
            collected = new HashSet<string>();
            placed.ResourcesCollectedByPlayer[State.CurrentPlayerIndex] = collected;
        }
        if (collected.Contains(resourceKey)) return false; // already collected this visit

        State.Resources[resourceKey] = State.Resources.GetValueOrDefault(resourceKey) + resource.PerVisit;
        collected.Add(resourceKey);
        _log?.Append($"採集資源「{resource.Name}」＋{resource.PerVisit}。", TurnLogKind.Hit);
        return true;
    }

    public bool InvestCluesForProgress()
    {
        var ok = TileProgressService.TryInvestClues(State, _module, State.CurrentPlayer.Position);
        if (ok) _log?.Append("投入線索推進探索等級。", TurnLogKind.Hit);
        return ok;
    }

    // ─── Event triggers ───────────────────────────────────────────────

    private bool TryTriggerOnEnterTile()
    {
        if (_scheduler is null) return false;
        var pos = State.CurrentPlayer.Position;
        if (!State.TileMap.TryGetValue((pos.X, pos.Y), out var placed)) return false;
        var ev = _scheduler.FindTriggeredOnEnterTile(placed.TileId, State, _module);
        if (ev is null) return false;
        // B9 · Guard 命令：玩家綁定同伴若 HasGuardPending，吸收這一次 tileEnter 事件
        var player = State.CurrentPlayer;
        if (player.BoundCompanionId is { } cid)
        {
            var guard = State.Companions.FirstOrDefault(c => c.CompanionId == cid);
            if (guard is not null && guard.HasGuardPending)
            {
                guard.HasGuardPending = false;
                _log?.Append($"同伴守衛啟動：阻擋了事件「{ev.Name}」。", TurnLogKind.Neutral);
                return false;
            }
        }
        BeginEvent(ev);
        return true;
    }

    /// <summary>
    /// D4 · 保底事件觸發門檻：連續 N 個大回合沒觸發事件時 EventCheck 強制抽一個
    /// `tags: random-filler` 事件。3 是避免玩家長時間空轉的平衡值。
    /// </summary>
    public const int FallbackEventThreshold = 3;

    private bool TryTriggerOnEventCheck()
    {
        // Task 9 · ORBIT 路徑（若有掛載）：優先處理已 register 到軌道上的事件，
        // 第一張被觸發的 → 走 PendingEvent 給玩家結算，其餘排入 PendingEventQueue。
        if (_orbit is not null && _orbit.Orbit.Pending.Count > 0)
        {
            var ctx = JsonLogicContextBuilder.FromGameState(State, _module, _orbit.Orbit);
            var report = _orbit.ResolvePending(ctx);
            if (report.EndingTriggered is not null)
            {
                State.BigRoundsWithoutEvent = 0;
                BeginEvent(report.EndingTriggered.Card);
                return true;
            }
            if (report.Triggered.Count > 0)
            {
                State.BigRoundsWithoutEvent = 0;
                BeginEvent(report.Triggered[0].Card);
                for (int i = 1; i < report.Triggered.Count; i++)
                    State.PendingEventQueue.Add(report.Triggered[i].Card.Id);
                return true;
            }
            // ORBIT 沒觸發 → 落到舊 scheduler 路徑（保留舊行為）
        }

        if (_scheduler is null) return false;
        var ev = _scheduler.FindTriggeredOnEventCheck(State, _module);
        if (ev is null)
        {
            // D4 · 沒觸發事件 → 計數 +1；達門檻則從 `random-filler` 事件池強制抽一個。
            State.BigRoundsWithoutEvent++;
            if (State.BigRoundsWithoutEvent >= FallbackEventThreshold)
            {
                ev = PickFallbackEvent();
                if (ev is null) return false;
                _log?.Append($"節奏保底 · 強制觸發 filler 事件（連續 {State.BigRoundsWithoutEvent} 大回合無事件）。", TurnLogKind.Neutral);
                State.BigRoundsWithoutEvent = 0;
                BeginEvent(ev);
                return true;
            }
            return false;
        }
        State.BigRoundsWithoutEvent = 0;
        BeginEvent(ev);
        return true;
    }

    /// <summary>
    /// D4 · 從模組事件庫中挑一張 tag 含 "random-filler" 且未被 ConsumedEventIds 封存的事件。
    /// 若本次大回合先前已挑過同一個則嘗試其他；皆無可選則回 null（不強制）。
    /// 決定性：用 RngSeed + CurrentBigRound 作 seed，避免存檔重播分歧。
    /// </summary>
    private EventCard? PickFallbackEvent()
    {
        var candidates = _module.Events.Values
            .Where(e => e.Tags.Contains("random-filler")
                        && !State.ConsumedEventIds.Contains(e.Id))
            .ToList();
        if (candidates.Count == 0) return null;
        int seed = unchecked(State.RngSeed ^ (State.CurrentBigRound * 33331));
        var rng = new Random(seed);
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>Queue an additional event behind the currently pending one (used for multi-event triggers).</summary>
    public void EnqueueEvent(EventCard ev)
    {
        if (PendingEvent is null) { BeginEvent(ev); return; }
        State.PendingEventQueue.Add(ev.Id);
    }

    private void BeginEvent(EventCard ev)
    {
        PendingEvent = ev;
        _log?.Append($"觸發事件「{ev.Name}」。", TurnLogKind.Neutral);
        // §3-6: first enter event → refill hand to limit
        RefillHand(TurnLoop.HandSizeLimit);
        // per-event cooldown counters reset (per-party shared)
        State.ActionCardUsesThisEvent.Clear();
        // A3 · OnEventStart 同伴 Specialty（例：對應類型事件 +1 擲骰）
        _companionAbilities.Fire(SpecialtyTrigger.OnEventStart, State, _module, ev);
    }

    public void ResolvePendingEvent()
    {
        if (PendingEvent is null)
            throw new InvalidOperationException("no pending event to resolve");
        PendingEvent = null;

        // §3-6: between multi-event triggers, refill up to 2 cards (no full refill-to-limit)
        if (State.PendingEventQueue.Count > 0)
        {
            RefillHand(State.CurrentPlayer.Hand.Count + 2);
            var nextId = State.PendingEventQueue[0];
            State.PendingEventQueue.RemoveAt(0);
            if (_module.Events.TryGetValue(nextId, out var nextEv))
            {
                PendingEvent = nextEv;
                State.ActionCardUsesThisEvent.Clear();
            }
            return;
        }

        // Move+Action merged. Phase on entry distinguishes trigger source:
        //   Action / Move → onEnter event (from Move()) → return to Action
        //   EventCheck   → eventCheck trigger (from Advance) → auto-chain to MapExpand
        State.Phase = State.Phase switch
        {
            TurnPhase.Move   => TurnPhase.Action,  // legacy save → Action
            TurnPhase.Action => TurnPhase.Action,  // onEnter event: stay in Action
            _                => State.Phase        // EventCheck stays for auto-chain
        };
        AdvanceAutoPhases(); // chain EventCheck → MapExpand (or further if empty)
    }

    // ─── Draw / turn boundary ─────────────────────────────────────────

    private TurnPhase DoDraw()
    {
        // A3 · BigRoundStart 觸發同伴 Specialty（在 AP / Hand refresh 前，以確保
        // healLowestHp 於玩家 HP 狀態被讀到）
        _companionAbilities.Fire(SpecialtyTrigger.BigRoundStart, State, _module);

        var player = State.CurrentPlayer;
        player.ActionPoints = ActionPointsPerTurn;
        player.MovesThisTurn = 0;
        State.UsedFamiliarFreeMoveThisTurn = false;
        player.ActionCardUsesThisTurn.Clear();
        // A1 · 每回合重置 Mulligan / Scout 使用旗標
        player.UsedMulliganThisTurn = false;
        player.UsedScoutThisTurn = false;
        // B9 · 每大回合重置同伴命令冷卻（未消耗的 Guard 也過期）
        CompanionCommandService.ResetPerBigRound(State);
        if (player.BoundCompanionId is { } cid
            && _module.NpcCompanions.TryGetValue(cid, out var companion))
        {
            player.ActionPoints += companion.ActionPoints;
        }

        RefillHand(HandSizeLimit);

        // A2 · Draw 結束後，綁定同伴各執行 1 個自主動作
        ExecuteCompanionTurns();

        return TurnPhase.Action;
    }

    /// <summary>
    /// A2 · 綁定同伴在 Draw 後、玩家輸入前，各執行 1 個自主動作（不扣 AP）。
    /// 動作由 NpcAi.PlanTurn 決定：PlayCard → Collect → Idle 優先序。
    /// </summary>
    private void ExecuteCompanionTurns()
    {
        foreach (var companion in State.Companions.Where(c => c.Hp > 0))
        {
            var player = State.Players.FirstOrDefault(
                p => p.BoundCompanionId == companion.CompanionId);
            if (player is null) continue;
            if (!_module.NpcCompanions.TryGetValue(companion.CompanionId, out var npc))
                continue;

            var scene = DetermineCompanionScene(player);
            var action = _npcAi.PlanTurn(npc, scene, State, _module, player);
            ApplyCompanionAction(companion, npc, player, action);
        }
    }

    private string DetermineCompanionScene(PlayerState player)
    {
        if (State.PendingBattleId is not null) return "battle";
        // 優先以 pending event 類型為 scene；否則依 tile terrain 粗分
        foreach (var eid in State.PendingEventQueue)
        {
            if (_module.Events.TryGetValue(eid, out var ev))
                return ev.Type.ToString().ToLowerInvariant();
        }
        if (State.TileMap.TryGetValue((player.Position.X, player.Position.Y), out var placed)
            && _module.Tiles.TryGetValue(placed.TileId, out var tile))
        {
            return tile.Terrain switch
            {
                Terrain.Town => "negotiation",
                Terrain.Wilderness => "exploration",
                Terrain.Dungeon => "battle",
                _ => "exploration"
            };
        }
        return "exploration";
    }

    private void ApplyCompanionAction(
        CompanionState companion, NpcCompanion npc, PlayerState player, CompanionAction action)
    {
        switch (action.Kind)
        {
            case CompanionActionKind.PlayCard
                when action.CardId is string cardId
                     && _module.ActionCards.TryGetValue(cardId, out var card):
                ApplyCompanionPlayCard(npc, player, card);
                break;

            case CompanionActionKind.Collect
                when action.ResourceKey is string key:
                ApplyCompanionCollect(npc, player, key);
                break;

            default:
                _log?.Append(action.NarrativeHint, TurnLogKind.Neutral);
                break;
        }
    }

    private void ApplyCompanionPlayCard(NpcCompanion npc, PlayerState player, ActionCard card)
    {
        // 不扣 AP，但移手牌 + 計冷卻
        player.Hand.Remove(card.Id);
        player.Discard.Add(card.Id);
        player.ActionCardUsesThisTurn[card.Id] =
            player.ActionCardUsesThisTurn.GetValueOrDefault(card.Id) + 1;
        State.ActionCardUsesThisEvent[card.Id] =
            State.ActionCardUsesThisEvent.GetValueOrDefault(card.Id) + 1;

        // 首張加成仍生效（對玩家有利，讓同伴出卡也推 tile level）
        TileProgressService.Advance(State, _module, player.Position, 1, ProgressReason.ActionCard);

        int npcStat = npc.Stats.Skill; // 簡化：以 Skill 為基（與玩家 PlayCard 一致）
        int modifier = TileModifierService.ComputeActionCheckModifier(
            _module, State, player.Position, card.Type);
        LastRoll = _dice.Roll2d6();
        int total = LastRoll.Value.Total + npcStat + modifier;
        bool success = total >= 8;
        _log?.Append(
            $"{npc.Name}：{card.Name} → {(success ? "成功" : "失敗")} "
            + $"(2d6={LastRoll.Value.Total}＋Skill={npcStat}＋地塊={modifier} = {total} vs 8)",
            success ? TurnLogKind.Hit : TurnLogKind.Miss);
    }

    private void ApplyCompanionCollect(NpcCompanion npc, PlayerState player, string resourceKey)
    {
        if (!State.TileMap.TryGetValue((player.Position.X, player.Position.Y), out var placed))
            return;
        if (!_module.Tiles.TryGetValue(placed.TileId, out var tile)) return;
        var resource = tile.Resources.FirstOrDefault(r => r.Key == resourceKey);
        if (resource is null) return;

        State.Resources[resourceKey] =
            State.Resources.GetValueOrDefault(resourceKey) + resource.PerVisit;
        int playerIdx = State.Players.IndexOf(player);
        if (!placed.ResourcesCollectedByPlayer.TryGetValue(playerIdx, out var set))
        {
            set = new HashSet<string>();
            placed.ResourcesCollectedByPlayer[playerIdx] = set;
        }
        set.Add(resourceKey);
        _log?.Append(
            $"{npc.Name} 協助採集 {resource.Name} ×{resource.PerVisit}",
            TurnLogKind.Neutral);
    }

    /// <summary>Draw from deck (reshuffling discard if empty) until hand reaches <paramref name="target"/> or both piles are empty. Target is capped at HandSizeLimit.</summary>
    private void RefillHand(int target)
    {
        var player = State.CurrentPlayer;
        int cap = Math.Min(target, HandSizeLimit);
        while (player.Hand.Count < cap)
        {
            if (player.Deck.Count == 0)
            {
                if (player.Discard.Count == 0) break;
                ReshuffleDiscardIntoDeck(player);
            }
            var card = player.Deck[0];
            player.Deck.RemoveAt(0);
            player.Hand.Add(card);
        }
    }

    private void ReshuffleDiscardIntoDeck(PlayerState player)
    {
        // Deterministic reshuffle based on seed + big round + player index
        int seed = unchecked(State.RngSeed
                             ^ (State.CurrentBigRound * 7919)
                             ^ (State.CurrentPlayerIndex * 104729));
        var rng = new Random(seed);
        var list = player.Discard.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        player.Deck.AddRange(list);
        player.Discard.Clear();
    }

    private TurnPhase BeginNextTurnAndReturnPhase()
    {
        EndPlayerTurn();
        return TurnPhase.Draw;
    }

    public void EndPlayerTurn()
    {
        // A3 · BigRoundEnd 同伴 Specialty
        _companionAbilities.Fire(SpecialtyTrigger.BigRoundEnd, State, _module);

        State.CurrentPlayer.ActionPoints = 0;
        State.CurrentPlayer.MovesThisTurn = 0;
        State.UsedFamiliarFreeMoveThisTurn = false;
        State.CurrentPlayerIndex++;
        if (State.CurrentPlayerIndex >= State.Players.Count)
        {
            State.CurrentPlayerIndex = 0;
            State.CurrentBigRound++;
            TileProgressService.ResetBigRound(State);
        }
        State.Phase = TurnPhase.Draw;
        PendingEvent = null;
    }

    // ─── PlayCard ─────────────────────────────────────────────────────

    public bool PlayCard(string actionCardId, Stat stat, int tn)
    {
        // Move+Action are merged — both phases accept card play (Move retained for legacy saves).
        if (State.Phase != TurnPhase.Action && State.Phase != TurnPhase.Move)
            throw new InvalidOperationException($"cannot play card outside Action phase (current: {State.Phase})");

        if (!_module.ActionCards.TryGetValue(actionCardId, out var card))
            throw new ArgumentException($"unknown action card '{actionCardId}'");

        var player = State.CurrentPlayer;
        if (!player.Hand.Contains(actionCardId))
            throw new InvalidOperationException($"action card '{actionCardId}' not in player's hand");

        // AllowedActionTypes check based on current tile
        if (State.TileMap.TryGetValue((player.Position.X, player.Position.Y), out var placed)
            && _module.Tiles.TryGetValue(placed.TileId, out var tile)
            && tile.AllowedActionTypes.Count > 0
            && !tile.AllowedActionTypes.Contains(card.Type))
        {
            throw new InvalidOperationException(
                $"action type '{card.Type}' is not allowed on tile '{tile.Id}'");
        }

        if (player.ActionPoints < card.Cost)
            throw new InvalidOperationException(
                $"not enough action points: have {player.ActionPoints}, need {card.Cost}");

        if (card.UsesPerTurn is int tMax)
        {
            int used = player.ActionCardUsesThisTurn.GetValueOrDefault(actionCardId);
            if (used >= tMax)
                throw new InvalidOperationException(
                    $"action card '{actionCardId}' per-turn limit reached ({used}/{tMax})");
        }
        if (card.UsesPerEvent is int eMax)
        {
            int used = State.ActionCardUsesThisEvent.GetValueOrDefault(actionCardId);
            if (used >= eMax)
                throw new InvalidOperationException(
                    $"action card '{actionCardId}' per-event limit reached ({used}/{eMax})");
        }

        player.Hand.Remove(actionCardId);
        player.Discard.Add(actionCardId);
        player.ActionPoints -= card.Cost;
        player.ActionCardUsesThisTurn[actionCardId] =
            player.ActionCardUsesThisTurn.GetValueOrDefault(actionCardId) + 1;
        State.ActionCardUsesThisEvent[actionCardId] =
            State.ActionCardUsesThisEvent.GetValueOrDefault(actionCardId) + 1;

        var character = _module.Characters[player.CharacterId];
        var statValue = stat switch
        {
            Stat.Power     => character.Stats.Power,
            Stat.Social    => character.Stats.Social,
            Stat.Skill     => character.Stats.Skill,
            Stat.Intellect => character.Stats.Intellect,
            _ => 0
        };

        int modifier = TileModifierService.ComputeActionCheckModifier(
            _module, State, player.Position, card.Type);

        int equipmentBonus = EquipmentService.GetStatBonus(player, _module, stat);

        // First action card played on this visit promotes tile exploration by +1
        TileProgressService.Advance(State, _module, player.Position, 1, ProgressReason.ActionCard);

        LastRoll = _dice.Roll2d6();
        // A3 · 納入 Specialty 擲骰加值；NextRoll 條目用一次後清除
        int specialtyBonus = State.PendingRollBonuses.Sum(b => b.Amount);
        // C5 · Focus 指令 pending +2；消費後歸零（跨大回合生效）
        int focusBonus = player.FocusBonusPending;
        int total = LastRoll.Value.Total + statValue + equipmentBonus + modifier + specialtyBonus + focusBonus;
        bool success = total >= tn;
        State.PendingRollBonuses.RemoveAll(
            b => b.Duration == SpecialtyEffectDuration.NextRoll);
        player.FocusBonusPending = 0;
        var bonusLog = specialtyBonus != 0 ? $"＋特長={specialtyBonus}" : "";
        var focusLog = focusBonus != 0 ? $"＋專注={focusBonus}" : "";
        _log?.Append(
            $"行動卡「{card.Name}」：2d6={LastRoll.Value.Total}＋{stat}={statValue}＋裝備={equipmentBonus}＋地塊={modifier}{bonusLog}{focusLog} = {total} vs TN {tn} → {(success ? "成功" : "失敗")}。",
            success ? TurnLogKind.Hit : TurnLogKind.Miss);

        // C9 · 事件中出卡加 roll：若當前有 pending event，這次成功出卡對該事件的後續 roll 加 bonus。
        //   bonus = 2（success）/ 1（failure）— 讓事件中出卡有實質影響而非只套 side effects。
        //   Duration=ThisEvent 確保跨多次出卡可疊加、事件結算後清除。
        if (PendingEvent is not null)
        {
            int eventBonus = success ? 2 : 1;
            State.PendingRollBonuses.Add(new PendingRollBonus
            {
                Amount = eventBonus,
                Duration = SpecialtyEffectDuration.ThisEvent,
                SourceCompanionId = $"action-card:{card.Id}",
            });
            _log?.Append(
                $"事件中出卡 → 後續事件擲骰 +{eventBonus}（本事件內有效）。",
                TurnLogKind.Neutral);
        }

        return success;
    }

    public bool IsGameOver(out EndReason? reason)
    {
        if (State.CurrentBigRound > State.MaxBigRounds)
        {
            reason = EndReason.TurnLimitReached;
            return true;
        }

        if (State.Players.All(p => p.Hp <= 0))
        {
            reason = EndReason.AllPlayersDown;
            return true;
        }

        reason = null;
        return false;
    }

    // ─── A1 · Rest / Mulligan / Scout ─────────────────────────────────
    //
    // 三類低成本動作解 Action 階段空洞。行動階段內詳見
    // docs/行動階段內容密度分析.md Part 4 P1。

    /// <summary>
    /// 休息：將剩餘 AP 歸零，恢復 2 HP（封頂 HpMax），結束本回合。
    /// 用於手牌卡死 / 沒事做時的主動結束方式。
    /// </summary>
    public void Rest()
    {
        if (State.Phase != TurnPhase.Action && State.Phase != TurnPhase.Move)
            throw new InvalidOperationException("can only rest in Action phase");
        var p = State.CurrentPlayer;
        int before = p.Hp;
        p.Hp = Math.Min(p.HpMax, p.Hp + 2);
        p.ActionPoints = 0;
        _log?.Append($"休息：HP {before} → {p.Hp}/{p.HpMax}，本回合結束。", TurnLogKind.Neutral);
        EndPlayerTurn();
    }

    /// <summary>
    /// 整理手牌：從手牌中找出至多 2 張當前不能打的卡，丟棄並抽等量新卡。
    /// 費 1 AP；每回合 1 次；若無不可打卡則丟出例外（UI 應先以 CanMulligan 判斷）。
    /// </summary>
    public int Mulligan()
    {
        if (State.Phase != TurnPhase.Action && State.Phase != TurnPhase.Move)
            throw new InvalidOperationException("can only mulligan in Action phase");
        var p = State.CurrentPlayer;
        if (p.UsedMulliganThisTurn)
            throw new InvalidOperationException("already mulliganed this turn");
        if (p.ActionPoints < 1)
            throw new InvalidOperationException("need 1 AP for mulligan");

        var disabled = p.Hand.Where(id => !CanCurrentlyPlay(id)).Take(2).ToList();
        if (disabled.Count == 0)
            throw new InvalidOperationException("no disabled cards to mulligan");

        foreach (var id in disabled)
        {
            p.Hand.Remove(id);
            p.Discard.Add(id);
        }
        p.ActionPoints -= 1;
        p.UsedMulliganThisTurn = true;
        int target = Math.Min(HandSizeLimit, p.Hand.Count + disabled.Count);
        RefillHand(target);
        _log?.Append($"整理手牌：棄 {disabled.Count} 張不可打的卡，補抽等量。", TurnLogKind.Neutral);
        return disabled.Count;
    }

    /// <summary>
    /// 偵察：免費動作（0 AP）；揭露地塊牌堆頂 2 張與地圖上可觸發事件的來源 tile。
    /// 每回合 1 次；TurnLog 記結果；用於路線規劃。
    /// </summary>
    public ScoutResult Scout()
    {
        if (State.Phase != TurnPhase.Action && State.Phase != TurnPhase.Move)
            throw new InvalidOperationException("can only scout in Action phase");
        var p = State.CurrentPlayer;
        if (p.UsedScoutThisTurn)
            throw new InvalidOperationException("already scouted this turn");

        var deckPeek = State.TileDeck.Take(2)
            .Select(id => _module.Tiles.TryGetValue(id, out var t) ? t.Name : id)
            .ToList();
        var placedTileIds = State.TileMap.Values.Select(pl => pl.TileId).ToHashSet();
        var upcoming = new List<(string EventName, string TileId, string TileName)>();
        foreach (var ev in _module.Events.Values)
        {
            if (State.ConsumedEventIds.Contains(ev.Id)) continue;
            if (ev.Trigger is not TileEnterTrigger t) continue;
            if (!placedTileIds.Contains(t.TileId)) continue;
            var name = _module.Tiles.TryGetValue(t.TileId, out var tile) ? tile.Name : t.TileId;
            upcoming.Add((ev.Name, t.TileId, name));
            if (upcoming.Count >= 3) break;
        }

        p.UsedScoutThisTurn = true;

        var deckText = deckPeek.Count > 0 ? string.Join(" → ", deckPeek) : "（牌堆空）";
        var eventText = upcoming.Count > 0
            ? string.Join("、", upcoming.Select(u => $"{u.EventName}@{u.TileName}"))
            : "（無明顯預兆）";
        _log?.Append($"偵察：下張地塊 [{deckText}]；可能觸發的事件：{eventText}。", TurnLogKind.Neutral);
        return new ScoutResult(deckPeek, upcoming);
    }

    // ─── C5 · Focus + 換裝備（行動階段 P1 補完）──────────────────────────
    // docs/行動階段內容密度分析.md Part 4 P1 後 2 項；§10.4.1 規格。

    /// <summary>
    /// 專注：費 1 AP → FocusBonusPending=2；下次擲骰 (PlayCard / EventResolver.Resolve / Battle roll)
    /// +2；消費後歸零。每回合多次可疊加（取最新）— 防呆：已有 pending 時依然可再下，覆蓋而非累加。
    /// </summary>
    public void Focus()
    {
        if (State.Phase != TurnPhase.Action && State.Phase != TurnPhase.Move)
            throw new InvalidOperationException("can only focus in Action phase");
        var p = State.CurrentPlayer;
        if (p.ActionPoints < 1)
            throw new InvalidOperationException("need 1 AP for focus");
        p.ActionPoints -= 1;
        p.FocusBonusPending = 2;
        _log?.Append("專注：下次擲骰 +2。", TurnLogKind.Neutral);
    }

    /// <summary>C5 · Focus 是否可下指令（UI CanExecute 綁定）。</summary>
    public bool CanFocus()
    {
        if (State.Phase != TurnPhase.Action && State.Phase != TurnPhase.Move) return false;
        return State.CurrentPlayer.ActionPoints >= 1;
    }

    /// <summary>
    /// 交換兩個裝備槽的裝備（例：MainHand ↔ Secondary）。費 1 AP。
    /// 用於戰鬥中臨時換武器、切配件等。槽內容可為 null（允許把空槽交換為有內容的槽）。
    /// </summary>
    public void SwapEquipment(EquipmentSlot slotA, EquipmentSlot slotB)
    {
        if (State.Phase != TurnPhase.Action && State.Phase != TurnPhase.Move)
            throw new InvalidOperationException("can only swap equipment in Action phase");
        if (slotA == slotB)
            throw new InvalidOperationException("slots must differ");
        var p = State.CurrentPlayer;
        if (p.ActionPoints < 1)
            throw new InvalidOperationException("need 1 AP for swap equipment");
        p.Equipment.TryGetValue(slotA, out var itemA);
        p.Equipment.TryGetValue(slotB, out var itemB);
        if (itemA is null && itemB is null)
            throw new InvalidOperationException("both slots empty");
        p.Equipment[slotA] = itemB;
        p.Equipment[slotB] = itemA;
        p.ActionPoints -= 1;
        _log?.Append($"換裝備：{slotA} ↔ {slotB}。", TurnLogKind.Neutral);
    }

    /// <summary>判斷某手牌 id 在當前狀態下是否能直接出牌（供 Mulligan 篩選）。</summary>
    private bool CanCurrentlyPlay(string cardId)
    {
        if (!_module.ActionCards.TryGetValue(cardId, out var card)) return false;
        var p = State.CurrentPlayer;
        if (p.ActionPoints < card.Cost) return false;
        if (State.TileMap.TryGetValue((p.Position.X, p.Position.Y), out var placed)
            && _module.Tiles.TryGetValue(placed.TileId, out var tile)
            && tile.AllowedActionTypes.Count > 0
            && !tile.AllowedActionTypes.Contains(card.Type))
            return false;
        if (card.UsesPerTurn is int tmax
            && p.ActionCardUsesThisTurn.GetValueOrDefault(cardId) >= tmax)
            return false;
        if (card.UsesPerEvent is int emax
            && State.ActionCardUsesThisEvent.GetValueOrDefault(cardId) >= emax)
            return false;
        return true;
    }
}

/// <summary>A1 · Scout 結果：牌堆頂預覽與可觸發事件清單。</summary>
public sealed record ScoutResult(
    IReadOnlyList<string> NextTileNames,
    IReadOnlyList<(string EventName, string TileId, string TileName)> UpcomingEventTiles);
