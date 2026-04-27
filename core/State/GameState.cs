// GameState — 主遊戲狀態（大回合流程以外的全部可變資料）。
// 內含：TurnPhase、Position、PlayerState（手/牌庫/棄牌/裝備/位置）、CompanionState、
// PlacedTile（探索等級/推進/採集）、TileMap、TileDeck、Flags、Resources、
// 事件冷卻/事件佇列、climax TN 加值。
// CreateNew 負責初始化：個人牌庫洗牌（Fisher–Yates 以 seed 決定性）、地塊牌庫採模組固定順序、
// 置起始地塊、綁 companion、套 character card slot。
using System.Text.Json;
using CardNarrative.Core.Models;

namespace CardNarrative.Core.State;

public enum TurnPhase
{
    Draw,
    MapExpand,
    /// <summary>Legacy: merged into <see cref="Action"/>. Retained so older saves deserialize;
    /// SaveService rewrites <see cref="Move"/> to <see cref="Action"/> on load.</summary>
    [Obsolete("Move and Action are merged into a single user-input phase; use Action.")]
    Move,
    Action,
    EventCheck,
    TurnEnd
}

public enum EndReason
{
    TurnLimitReached,
    AllPlayersDown,
    VictoryAchieved
}

public sealed record Position(int X, int Y);

public sealed class PlayerState
{
    public required string CharacterId { get; init; }
    public int Hp { get; set; }
    public int HpMax { get; init; }
    public int ActionPoints { get; set; }
    public int MovesThisTurn { get; set; }
    /// <summary>
    /// PR-B · 本回合已用觀察次數（本回合首次觀察免費；之後每次 2 AP）。
    /// 規格書 §3.1.4 觀察規則。對齊 <see cref="MovesThisTurn"/> 命名。
    /// 由 WorldMap.FirstObserveUsedThisTurn dual-mode dispatch 讀寫。AdvanceTurn 重置為 0。
    /// </summary>
    public int ObservesThisTurn { get; set; }
    public int Contributions { get; set; }
    public List<string> Hand { get; } = new();
    public List<string> Deck { get; } = new();
    public List<string> Discard { get; } = new();
    public Dictionary<string, int> ActionCardUsesThisTurn { get; } = new();
    public Position Position { get; set; } = new(0, 0);

    /// <summary>
    /// PR-B · 玩家當前持有的地塊 id（MapExpand 模式抽出但尚未放置）。
    /// null = 待命狀態無持有地塊。runtime 透過 WorldMap.HeldTile / HeldTileId 投影。
    /// 任務 11 前曾在 WorldMap._heldTileId 為唯一 SoT；任務 11 PR-B 統一收歸 GameState，
    /// 為 Task 17 存檔讀檔保留持有狀態鋪路（玩家在 MapExpand 模式存檔時不丟失）。
    /// </summary>
    public string? HeldTileId { get; set; }

    public Dictionary<EquipmentSlot, string?> Equipment { get; } = new();
    public EquipmentSlot CharacterCardSlot { get; set; } = EquipmentSlot.Head;
    /// <summary>
    /// PR-A · 規格書 §3.4.3「獲得即入背包」：玩家背包（上限 EquipmentManager.BackpackMax = 3）。
    /// 由 WorldMap dual-mode dispatch 讀寫；EffectHandler.ApplyGrantEquipment 在自動裝備失敗時優先寫此欄位，
    /// 滿了才退回 PendingEquipmentGrants。Task 11 前曾在 WorldMap._backpack 為唯一 SoT；任務 11 PR-A 統一收歸 GameState。
    /// </summary>
    public List<string> Backpack { get; } = new();
    public List<string> PendingEquipmentGrants { get; } = new();
    public string? BoundCompanionId { get; set; }

    /// <summary>A1 · 本回合是否已使用「整理手牌」（每回合 1 次）。DoDraw 重置。</summary>
    public bool UsedMulliganThisTurn { get; set; }
    /// <summary>A1 · 本回合是否已使用「偵察」（每回合 1 次）。DoDraw 重置。</summary>
    public bool UsedScoutThisTurn { get; set; }

    /// <summary>
    /// C5 · Focus（專注）buff：下一次擲骰 +2。Focus 指令費 1 AP 設置；任何擲骰消費後歸零。
    /// 跨大回合保留（設置後下回合開始也能使用，但 Focus 指令僅在 Action 階段可下）。
    /// </summary>
    public int FocusBonusPending { get; set; }
}

/// <summary>
/// Stage 5 · 戰鬥中同伴可提供的 3 種輔助（規格 §1.7）。每戰每同伴每種輔助 1 次冷卻。
/// </summary>
public enum CompanionCombatSupportKind
{
    /// <summary>攻擊加乘：玩家下次攻擊命中時傷害 +2。</summary>
    AttackBoost,
    /// <summary>行動輔助：玩家下次擲骰（攻擊 / 迴避 / 格擋 / 反擊）+2。</summary>
    RollSupport,
    /// <summary>抵擋傷害（蓄勢）：玩家下次受擊時由同伴代受全額傷害。</summary>
    BlockDamage,
}

/// <summary>
/// B9 · 同伴命令類型：玩家主動指揮同伴消耗其 AP 執行戰術動作。
/// 每種命令有獨立冷卻（CommandsUsedThisBigRound / CommandsUsedThisVisit）。
/// </summary>
public enum CompanionCommandKind
{
    /// <summary>偵察：揭露相鄰 placeholder tile 的 Level 或預覽 TileDeck。無冷卻。</summary>
    Scout,
    /// <summary>搜尋：幫玩家採集當前 tile 一項資源（獨立於玩家自身採集次數）。每次訪問 1 次。</summary>
    Search,
    /// <summary>守衛：下次玩家在此 tile 觸發的 tileEnter 遭遇被阻擋 1 次。每大回合 1 次。</summary>
    Guard,
    /// <summary>支援：玩家下次擲骰 +2。每大回合 1 次。</summary>
    Support,
}

public sealed class CompanionState
{
    public required string CompanionId { get; init; }
    public int Hp { get; set; }
    public int Contributions { get; set; }

    /// <summary>B9 · 本大回合已下過的命令集合。DoDraw 清空（每大回合限額重置）。</summary>
    public HashSet<CompanionCommandKind> CommandsUsedThisBigRound { get; } = new();

    /// <summary>
    /// B9 · 本次訪問已下過的命令集合（key = tileId；離開 tile 自動重置）。
    /// 用於 Search 的「每次訪問 1 次」限制。
    /// </summary>
    public Dictionary<string, HashSet<CompanionCommandKind>> CommandsUsedThisVisit { get; } = new();

    /// <summary>B9 · Guard 命令是否已下過、尚未消耗（玩家進下 tile 時觸發 onEnter 事件被阻擋 1 次）。</summary>
    public bool HasGuardPending { get; set; }

    /// <summary>Stage 5 · 本戰鬥已用過的戰鬥輔助種類集合（每戰每同伴 1 次冷卻；戰鬥結束後清除）。</summary>
    public HashSet<CompanionCombatSupportKind> UsedCombatSupportThisBattle { get; } = new();
}

public sealed class PlacedTile
{
    /// <summary>A6 · 整局 1 次類 interactions 已使用的 id 集合（跨訪問永久）。</summary>
    public HashSet<string> InteractionsUsedOncePerGame { get; } = new();
    /// <summary>A6 · 本次訪問已用的 interactions（離開 tile 重置）。key=playerIdx。</summary>
    public Dictionary<int, HashSet<string>> InteractionsUsedThisVisit { get; } = new();
    /// <summary>A6 · 本大回合已用的 interactions（TileProgressService.ResetBigRound 清零）。</summary>
    public Dictionary<int, HashSet<string>> InteractionsUsedThisBigRound { get; } = new();

    /// <summary>
    /// B2 · 改為 setter 供 TransformTileEffect 原地替換 TileId；
    /// 新物件仍需初始化子賦值（required 保留）。
    /// </summary>
    public required string TileId { get; set; }
    public ExplorationLevel Level { get; set; } = ExplorationLevel.Unknown;
    public int ProgressGainedThisBigRound { get; set; }
    public int LastProgressBigRound { get; set; }
    public bool ActionCardProgressUsedThisVisit { get; set; }
    public Dictionary<int, HashSet<string>> ResourcesCollectedByPlayer { get; } = new();
    public int LastVisitBigRound { get; set; }
}

public sealed class GameState
{
    public required int RngSeed { get; init; }
    public required int MaxBigRounds { get; init; }

    /// <summary>
    /// Phase 2 任務 11 Stage 0：地圖網格邊界（規格書 §1.5）。
    /// null = 無界（M-series 既有測試行為，2026-04 前的預設）；
    /// 設定後 (0..GridSize-1) × (0..GridSize-1) 為有效範圍，超出視為非法位置。
    /// runtime 透過 <see cref="CreateNew"/> 帶入 9 啟用；既有 xUnit 不帶則保持無界相容。
    /// </summary>
    public int? GridSize { get; init; }

    public int CurrentBigRound { get; set; } = 1;
    public int CurrentPlayerIndex { get; set; }
    public TurnPhase Phase { get; set; } = TurnPhase.Draw;

    public List<PlayerState> Players { get; } = new();
    public List<CompanionState> Companions { get; } = new();
    public Dictionary<string, JsonElement> Flags { get; } = new();
    public Dictionary<(int X, int Y), PlacedTile> TileMap { get; } = new();
    public List<string> TileDeck { get; } = new();
    public bool UsedFamiliarFreeMoveThisTurn { get; set; }
    /// <summary>Set once <see cref="Services.TurnLoop.TriggerStartingTileEntry"/> has applied
    /// the starting tile's OnEnter effects and tileEnter event. Guards against double-application.</summary>
    public bool StartingTileInitialized { get; set; }
    public Dictionary<string, int> Resources { get; } = new();
    public HashSet<string> ConsumedEventIds { get; } = new();
    public string? PendingBattleId { get; set; }
    public int ClimaxTnBonus { get; set; }
    public Dictionary<string, int> ActionCardUsesThisEvent { get; } = new();
    public List<string> PendingEventQueue { get; } = new();

    /// <summary>A3 · 擲骰加值待生效清單（由 Companion BonusRoll specialty 產生，
    /// 於 PlayCard / EventResolver.Resolve 時納入 total，NextRoll 持續時間用一次後清除）。</summary>
    public List<Services.PendingRollBonus> PendingRollBonuses { get; } = new();

    /// <summary>
    /// §13-1 S1 · 開場序幕 overlay 是否已對玩家顯示過。第一次進入主遊戲時由
    /// MainWindow 檢查：若為 false 則自動彈出 PrologueIntroView，結束後設為 true。
    /// Hamburger menu「查看序章」不受此旗影響，可重複打開。
    /// </summary>
    public bool HasSeenPrologue { get; set; }

    /// <summary>
    /// D4 · 節奏保底：連續無觸發事件的大回合計數。EventCheck 觸發事件時歸零；
    /// 未觸發時 +1；達 <see cref="Services.TurnLoop.FallbackEventThreshold"/> 時 EventCheck
    /// 強制抽一個 `tags: random-filler` 事件。避免玩家長時間無事件空轉。
    /// </summary>
    public int BigRoundsWithoutEvent { get; set; }

    public PlayerState CurrentPlayer => Players[CurrentPlayerIndex];

    /// <summary>
    /// Phase 2 任務 11 Stage 0：判斷座標是否在地圖網格內。
    /// <see cref="GridSize"/> 未設（null）視為無界，永遠回 true（M-series 相容路徑）。
    /// </summary>
    public bool IsInBounds(int x, int y)
    {
        if (!GridSize.HasValue) return true;
        return x >= 0 && x < GridSize.Value && y >= 0 && y < GridSize.Value;
    }

    public bool IsInBounds(Position pos) => IsInBounds(pos.X, pos.Y);

    public static GameState CreateNew(
        Module module,
        IReadOnlyList<string> chosenCharacterIds,
        IReadOnlyList<string> chosenCompanionIds,
        int seed,
        IReadOnlyDictionary<string, string>? companionToPlayerBindings = null,
        IReadOnlyDictionary<string, EquipmentSlot>? characterCardSlots = null,
        int? gridSize = null,
        Position? startPosition = null)
    {
        int turnLimit = module.Prologue.LoseConditions
            .OfType<TurnLimitLoseCondition>()
            .Select(c => c.Value)
            .DefaultIfEmpty(20)
            .First();

        var state = new GameState
        {
            RngSeed = seed,
            MaxBigRounds = turnLimit,
            GridSize = gridSize
        };

        // Stage 0：起始座標。null = 預設 (0,0) 維持 M-series 既有測試行為；
        // runtime（任務 11 起）一律帶 (4,4) 對齊 Phase 1+2 的 9×9 中心。
        var startPos = startPosition ?? new Position(0, 0);
        if (gridSize.HasValue && !state.IsInBounds(startPos))
            throw new ArgumentException(
                $"startPosition ({startPos.X},{startPos.Y}) is outside gridSize {gridSize.Value} bounds",
                nameof(startPosition));

        int playerIndex = 0;
        foreach (var id in chosenCharacterIds)
        {
            if (!module.Characters.TryGetValue(id, out var character))
                throw new ArgumentException($"character '{id}' not found in module");
            var ps = new PlayerState
            {
                CharacterId = id,
                Hp = character.HpMax,
                HpMax = character.HpMax,
                ActionPoints = 3,
                CharacterCardSlot = characterCardSlots is not null
                                    && characterCardSlots.TryGetValue(id, out var slot)
                    ? slot
                    : EquipmentSlot.Head
            };
            // Seed personal deck deterministically from character's StartingDeck
            var rng = new Random(unchecked(seed * 31 + playerIndex));
            ps.Deck.AddRange(FisherYates(character.StartingDeck, rng));
            state.Players.Add(ps);
            playerIndex++;
        }

        foreach (var id in chosenCompanionIds)
        {
            if (!module.NpcCompanions.TryGetValue(id, out var companion))
                throw new ArgumentException($"companion '{id}' not found in module");
            state.Companions.Add(new CompanionState
            {
                CompanionId = id,
                Hp = companion.Hp
            });
        }

        if (companionToPlayerBindings is not null)
        {
            foreach (var (companionId, playerCharId) in companionToPlayerBindings)
            {
                var player = state.Players.FirstOrDefault(p => p.CharacterId == playerCharId);
                if (player is null) continue;
                player.BoundCompanionId = companionId;
            }
        }

        var startId = module.Prologue.StartingTileId;
        state.TileMap[(startPos.X, startPos.Y)] = new PlacedTile { TileId = startId, Level = ExplorationLevel.Unfamiliar };
        foreach (var p in state.Players) p.Position = startPos;

        // Tile deck. Fixed order from module definition; tiles with Copies > 1
        // are seeded consecutively so subsequent copies draw back-to-back.
        // The starting tile already occupies startPos, so only its remaining copies
        // (if any) go into the deck.
        foreach (var (id, tile) in module.Tiles)
        {
            int copies = Math.Max(1, tile.Copies);
            int deckCopies = id == startId ? copies - 1 : copies;
            for (int i = 0; i < deckCopies; i++) state.TileDeck.Add(id);
        }

        return state;
    }

    private static List<T> FisherYates<T>(IEnumerable<T> source, Random rng)
    {
        var list = source.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
