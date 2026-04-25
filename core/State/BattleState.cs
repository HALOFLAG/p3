// BattleState — 戰鬥期間的獨立狀態機（由 BattleEngine 驅動）。
// Phase：Start → Encounter → PlayerTurn/EnemyTurn（含 AwaitingResponse 子階段）→ Victory/Defeat/EnemyFled。
// 追蹤敵方 HP、遭遇 Tier/Reveal、回合計數、玩家使用基本/角色能力旗標、敵人待揭露動作、Log。
using CardNarrative.Core.Models;

namespace CardNarrative.Core.State;

public enum BattlePhase
{
    Start,
    Encounter,
    PlayerTurn,
    EnemyTurn,
    AwaitingResponse,
    Victory,
    Defeat,
    EnemyFled
}

public enum EncounterTier
{
    Unresolved,
    Advantage,
    Normal,
    Ambushed
}

public enum RevealLevel
{
    None,
    Partial,
    Full
}

public sealed class BattleState
{
    public required string BattleId { get; init; }
    public required int EnemyHp { get; set; }
    public required int EnemyHpMax { get; init; }

    public BattlePhase Phase { get; set; } = BattlePhase.Start;
    public EncounterTier Tier { get; set; } = EncounterTier.Unresolved;
    public RevealLevel Reveal { get; set; } = RevealLevel.None;
    public bool PlayerGoesFirst { get; set; } = true;

    public int ActivePlayerIndex { get; set; }
    public int RoundNumber { get; set; }

    /// <summary>Extra damage applied to the next hit taken by a specific player (crit-miss carry-over).</summary>
    public Dictionary<int, int> PlayerNextHitBonusDamage { get; } = new();

    /// <summary>+X defensive bonus on player's next dodge roll (from Basic Reposition).</summary>
    public Dictionary<int, int> PlayerDodgeBonusNext { get; } = new();

    /// <summary>Evasion boost granted to a player for the remainder of this round (Basic Defend).</summary>
    public Dictionary<int, int> PlayerEvasionBonusThisRound { get; } = new();

    /// <summary>Tracks whether the current player's basic action (once-per-turn) is spent.</summary>
    public HashSet<int> UsedBasicActionThisTurn { get; } = new();

    /// <summary>Set of Character CombatAbility Ids already consumed in this battle (persist across rounds).</summary>
    public HashSet<string> UsedCharacterAbilities { get; } = new();

    /// <summary>First-turn AP penalty imposed by an Ambushed encounter (per player index).</summary>
    public Dictionary<int, int> PendingApPenalty { get; } = new();

    /// <summary>The enemy action currently awaiting player response (null when Phase != AwaitingResponse).</summary>
    public EnemyAction? PendingEnemyAction { get; set; }
    public int? PendingResponseTargetPlayerIndex { get; set; }

    /// <summary>Id of the EnemyAction the enemy used on the previous enemy turn (for PreparedLastTurn triggers).</summary>
    public string? LastEnemyActionId { get; set; }
    /// <summary>Enemy flagged as preparing; next Attack gets double damage.</summary>
    public bool EnemyPrepared { get; set; }

    public int? LastEnemyTargetIndex { get; set; }
    public List<string> Log { get; } = new();
}
