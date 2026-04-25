// BattleEngine — 戰鬥回合驅動（與 TurnLoop 解耦）。
// 新流程：Start → Encounter（一次擲骰決定 Tier + RevealLevel + 先攻）
//   → PlayerTurn（基本 / 角色 / 動作卡三類 ResolvePlayerAction）
//   ⇄ EnemyTurn（PlanEnemyAction 讀 EnemyAction[] 觸發+權重抽一）
//   → AwaitingResponse（玩家 Accept / Dodge / Block / Counter / Reflect 反應）
//   → Victory / Defeat / EnemyFled。
// 產出 *Resolution 記錄供 UI Log 呈現。
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public enum CheckDegree
{
    CritSuccess,
    Success,
    Partial,
    Fail,
    CritFail
}

public sealed record EncounterResolution(
    RollResult Roll,
    int Total,
    int Tn,
    EncounterTier Tier,
    RevealLevel Reveal,
    bool PlayerGoesFirst,
    string LogLine
);

public sealed record PlayerActionResolution(
    PlayerActionChoice Choice,
    RollResult? Roll,
    int? Total,
    CheckDegree Degree,
    int DamageDealt,
    int EvasionGranted,
    int DodgeBonusGranted,
    bool Retreated,
    string LogLine
);

public sealed record EnemyActionPlan(
    EnemyAction Action,
    int? TargetPlayerIndex,
    string LogLine
);

public sealed record EnemyActionResolution(
    EnemyAction Action,
    PlayerResponseChoice Response,
    RollResult? Roll,
    int? Total,
    int RawDamage,
    int DamageDealtToPlayer,
    int DamageReflectedToEnemy,
    int TargetPlayerIndex,
    bool Dodged,
    string LogLine
);

public enum BattleEndResult
{
    Ongoing,
    Victory,
    Defeat,
    EnemyFled
}

public sealed class BattleEngine
{
    private readonly IDiceService _dice;
    private readonly ITurnLogSink? _log;

    public BattleEngine(IDiceService dice, ITurnLogSink? log = null)
    {
        _dice = dice;
        _log = log;
    }

    private void RecordLog(BattleState bs, string line, TurnLogKind kind = TurnLogKind.Neutral)
    {
        bs.Log.Add(line);
        _log?.Append(line, kind);
    }

    // ─── Start ────────────────────────────────────────────────────────

    public BattleState Start(BattleCard card)
    {
        return new BattleState
        {
            BattleId = card.Id,
            EnemyHp = card.Stats.HpMax,
            EnemyHpMax = card.Stats.HpMax,
            Phase = BattlePhase.Encounter
        };
    }

    // ─── Phase A: Encounter ───────────────────────────────────────────

    /// <summary>
    /// Single encounter roll: 2d6 + Skill + Feet equipment Skill bonus.
    /// Tier is determined by the total relative to card.EncounterTn:
    ///   ≥ TN+3 or double6 → Advantage (player first, Reveal=Full)
    ///   ≥ TN               → Normal (player first, Reveal=Partial)
    ///   < TN or double1   → Ambushed (enemy first, Reveal=None, first-turn AP −1; double1 adds +2 next-hit-bonus)
    /// </summary>
    public EncounterResolution ResolveEncounter(
        BattleState bs, BattleCard card, PlayerState player, Character character, Module? module = null)
    {
        var roll = _dice.Roll2d6();
        int feetBonus = module is null ? 0 : EquipmentService.GetInitiativeBonus(player, module);
        int total = roll.Total + character.Stats.Skill + feetBonus;
        int tn = card.EncounterTn;

        EncounterTier tier;
        RevealLevel reveal;
        bool playerFirst;

        if (roll.IsDouble6 || total >= tn + 3)
        {
            tier = EncounterTier.Advantage;
            reveal = RevealLevel.Full;
            playerFirst = true;
        }
        else if (total >= tn && !roll.IsDouble1)
        {
            tier = EncounterTier.Normal;
            reveal = RevealLevel.Partial;
            playerFirst = true;
        }
        else
        {
            tier = EncounterTier.Ambushed;
            reveal = RevealLevel.None;
            playerFirst = false;
        }

        bs.Tier = tier;
        bs.Reveal = reveal;
        bs.PlayerGoesFirst = playerFirst;
        bs.Phase = playerFirst ? BattlePhase.PlayerTurn : BattlePhase.EnemyTurn;

        if (tier == EncounterTier.Ambushed)
        {
            for (int i = 0; i < 8; i++) bs.PendingApPenalty[i] = 1;
        }
        if (roll.IsDouble1)
        {
            bs.PlayerNextHitBonusDamage[bs.ActivePlayerIndex] =
                bs.PlayerNextHitBonusDamage.GetValueOrDefault(bs.ActivePlayerIndex) + 2;
        }

        string tierZh = tier switch
        {
            EncounterTier.Advantage => "優勢：完整揭露 + 玩家先攻",
            EncounterTier.Normal => "正常：部份揭露 + 玩家先攻",
            EncounterTier.Ambushed => "劣勢：情報全隱藏 + 敵方先攻，首回合 AP −1",
            _ => "未決"
        };
        string line = $"遭遇檢定 {total}（2d6={roll.Total}+Skill={character.Stats.Skill}+裝備={feetBonus}）vs TN {tn} → {tierZh}。";
        if (roll.IsDouble1) line += " 雙 1：下一次被擊中額外 +2 傷害。";
        if (roll.IsDouble6) line += " 雙 6：氣勢如虹。";
        var encounterKind = tier switch
        {
            EncounterTier.Advantage => TurnLogKind.Hit,
            EncounterTier.Ambushed => TurnLogKind.Miss,
            _ => TurnLogKind.Neutral
        };
        RecordLog(bs, line, encounterKind);

        return new EncounterResolution(roll, total, tn, tier, reveal, playerFirst, line);
    }

    // ─── Phase B: Player turn ─────────────────────────────────────────

    /// <summary>
    /// Resolve one player action from three sources (Basic / Character / ActionCard).
    /// Preconditions (the caller / VM should gate these):
    ///   - Basic: UsedBasicActionThisTurn does not contain ActivePlayerIndex.
    ///   - Character: Ability exists on character and not in UsedCharacterAbilities.
    ///   - ActionCard: Card is in player's hand, AP &gt;= Cost, and Combat effect is present (or defaulted RollBonus=0).
    /// This method mutates bs + player (HP, AP, hand, cooldowns, evasion, dodge bonus).
    /// </summary>
    public PlayerActionResolution ResolvePlayerAction(
        BattleState bs, BattleCard card, PlayerState player, Character character,
        PlayerActionChoice choice, GameState? gameState = null, Module? module = null)
    {
        return choice switch
        {
            BasicActionChoice b => ResolveBasic(bs, card, player, character, b, module),
            CharacterActionChoice ch => ResolveCharacterAbility(bs, card, player, character, ch, module),
            CardActionChoice cc => ResolveCard(bs, card, player, character, cc, gameState, module),
            _ => throw new ArgumentException($"unknown player action choice: {choice}")
        };
    }

    private PlayerActionResolution ResolveBasic(
        BattleState bs, BattleCard card, PlayerState player, Character character,
        BasicActionChoice choice, Module? module)
    {
        bs.UsedBasicActionThisTurn.Add(bs.ActivePlayerIndex);

        switch (choice.Kind)
        {
            case BasicActionKind.Attack:
                return RollAttack(bs, card, player, character, module,
                    rollBonus: 0, damageMultiplier: 1.0,
                    sourceLabel: "基本攻擊");

            case BasicActionKind.Defend:
                int evGrant = 2;
                bs.PlayerEvasionBonusThisRound[bs.ActivePlayerIndex] =
                    bs.PlayerEvasionBonusThisRound.GetValueOrDefault(bs.ActivePlayerIndex) + evGrant;
                string defendLine = $"玩家採取防禦姿態，本回合閃避 +{evGrant}。";
                RecordLog(bs, defendLine);
                return new PlayerActionResolution(choice, null, null, CheckDegree.Success,
                    DamageDealt: 0, EvasionGranted: evGrant, DodgeBonusGranted: 0,
                    Retreated: false, LogLine: defendLine);

            case BasicActionKind.Observe:
                var enc = ResolveEncounter(bs, card, player, character, module);
                bs.Phase = BattlePhase.PlayerTurn;
                return new PlayerActionResolution(choice, enc.Roll, enc.Total,
                    DegreeFromTier(enc.Tier), 0, 0, 0, false, enc.LogLine);

            case BasicActionKind.Reposition:
                int bonus = 2;
                bs.PlayerDodgeBonusNext[bs.ActivePlayerIndex] =
                    bs.PlayerDodgeBonusNext.GetValueOrDefault(bs.ActivePlayerIndex) + bonus;
                string rpLine = $"玩家重整位置，下次迴避 +{bonus}。";
                RecordLog(bs, rpLine);
                return new PlayerActionResolution(choice, null, null, CheckDegree.Success,
                    0, 0, bonus, false, rpLine);

            case BasicActionKind.Retreat:
                bs.Phase = BattlePhase.EnemyFled;
                string retLine = "玩家選擇撤退，戰鬥結束（非勝利）。";
                RecordLog(bs, retLine, TurnLogKind.Miss);
                return new PlayerActionResolution(choice, null, null, CheckDegree.Success,
                    0, 0, 0, true, retLine);

            default:
                throw new ArgumentOutOfRangeException(nameof(choice.Kind));
        }
    }

    private static CheckDegree DegreeFromTier(EncounterTier tier) => tier switch
    {
        EncounterTier.Advantage => CheckDegree.CritSuccess,
        EncounterTier.Normal => CheckDegree.Success,
        _ => CheckDegree.Fail
    };

    private PlayerActionResolution ResolveCharacterAbility(
        BattleState bs, BattleCard card, PlayerState player, Character character,
        CharacterActionChoice choice, Module? module)
    {
        var ability = character.CombatAbilities.FirstOrDefault(a => a.Id == choice.AbilityId)
            ?? throw new InvalidOperationException($"character '{character.Id}' has no ability '{choice.AbilityId}'");
        if (bs.UsedCharacterAbilities.Contains(ability.Id))
            throw new InvalidOperationException($"character ability '{ability.Id}' already used this battle");

        bs.UsedCharacterAbilities.Add(ability.Id);
        return RollAttack(bs, card, player, character, module,
            rollBonus: ability.RollBonus,
            damageMultiplier: ability.DamageMultiplier <= 0 ? 1.0 : ability.DamageMultiplier,
            sourceLabel: $"角色行動「{ability.Name}」");
    }

    private PlayerActionResolution ResolveCard(
        BattleState bs, BattleCard card, PlayerState player, Character character,
        CardActionChoice choice, GameState? gameState, Module? module)
    {
        if (module is null)
            throw new InvalidOperationException("ResolveCard requires module for action card lookup");
        if (!module.ActionCards.TryGetValue(choice.CardId, out var actionCard))
            throw new InvalidOperationException($"action card '{choice.CardId}' not found in module");
        if (!player.Hand.Contains(choice.CardId))
            throw new InvalidOperationException($"action card '{choice.CardId}' not in player's hand");
        if (player.ActionPoints < actionCard.Cost)
            throw new InvalidOperationException(
                $"not enough AP: have {player.ActionPoints}, need {actionCard.Cost}");

        // Consume AP and move card to discard.
        player.Hand.Remove(choice.CardId);
        player.Discard.Add(choice.CardId);
        player.ActionPoints -= actionCard.Cost;
        player.ActionCardUsesThisTurn[choice.CardId] =
            player.ActionCardUsesThisTurn.GetValueOrDefault(choice.CardId) + 1;
        if (gameState is not null)
            gameState.ActionCardUsesThisEvent[choice.CardId] =
                gameState.ActionCardUsesThisEvent.GetValueOrDefault(choice.CardId) + 1;

        var combat = actionCard.Combat;
        int rollBonus = combat is { Route: CombatEffectRoute.RollBonus } ? combat.RollBonus : 0;
        double mult = combat is { Route: CombatEffectRoute.DamageMultiplier }
            ? (combat.DamageMultiplier <= 0 ? 1.0 : combat.DamageMultiplier)
            : 1.0;

        return RollAttack(bs, card, player, character, module,
            rollBonus: rollBonus,
            damageMultiplier: mult,
            sourceLabel: $"動作卡「{actionCard.Name}」");
    }

    /// <summary>Common 2d6 + Power (+weapon hit + roll bonus) attack path used by Basic/Character/Card.</summary>
    private PlayerActionResolution RollAttack(
        BattleState bs, BattleCard card, PlayerState player, Character character,
        Module? module, int rollBonus, double damageMultiplier, string sourceLabel)
    {
        var roll = _dice.Roll2d6();
        var weapon = module is null ? null : EquipmentService.GetWeaponStats(player, module);
        int weaponHit = weapon?.HitBonus ?? 0;
        int weaponDmg = weapon?.Damage ?? 0;
        int power = character.Stats.Power;
        int total = roll.Total + power + weaponHit + rollBonus;

        int tn = card.Stats.Evasion;
        CheckDegree degree;
        bool hit;
        if (roll.IsDouble6) { degree = CheckDegree.CritSuccess; hit = true; }
        else if (roll.IsDouble1) { degree = CheckDegree.CritFail; hit = false; }
        else if (total >= tn) { degree = CheckDegree.Success; hit = true; }
        else if (total >= tn - 2) { degree = CheckDegree.Partial; hit = true; }
        else { degree = CheckDegree.Fail; hit = false; }

        int dmg = 0;
        if (hit)
        {
            int baseDmg = Math.Max(1, weaponDmg + power - card.Stats.Defense);
            double scaled = baseDmg * damageMultiplier;
            if (degree == CheckDegree.CritSuccess) scaled += 3;
            else if (degree == CheckDegree.Partial) scaled = Math.Max(1, Math.Floor(scaled / 2.0));
            dmg = Math.Max(1, (int)Math.Floor(scaled));
            bs.EnemyHp = Math.Max(0, bs.EnemyHp - dmg);
        }

        if (degree == CheckDegree.CritFail)
        {
            bs.PlayerNextHitBonusDamage[bs.ActivePlayerIndex] =
                bs.PlayerNextHitBonusDamage.GetValueOrDefault(bs.ActivePlayerIndex) + 2;
        }

        string degreeZh = degree switch
        {
            CheckDegree.CritSuccess => "爆擊",
            CheckDegree.Success => "命中",
            CheckDegree.Partial => "勉強命中",
            CheckDegree.Fail => "未命中",
            CheckDegree.CritFail => "大失敗",
            _ => degree.ToString()
        };
        string line = hit
            ? $"{sourceLabel}：{degreeZh}（{total} vs 閃避 {tn}）— 造成 {dmg} 傷害。"
            : degree == CheckDegree.CritFail
                ? $"{sourceLabel}：{degreeZh}（雙 1）— 自亂陣腳，下次被擊中 +2 傷害。"
                : $"{sourceLabel}：{degreeZh}（{total} vs 閃避 {tn}）。";
        RecordLog(bs, line, hit ? TurnLogKind.Hit : TurnLogKind.Miss);

        return new PlayerActionResolution(
            new BasicActionChoice(BasicActionKind.Attack), roll, total, degree,
            DamageDealt: dmg, EvasionGranted: 0, DodgeBonusGranted: 0,
            Retreated: false, LogLine: line);
    }

    // ─── Phase C: Enemy turn ──────────────────────────────────────────

    public EnemyActionPlan PlanEnemyAction(
        BattleState bs, BattleCard card, IReadOnlyList<PlayerState> players)
    {
        if (card.EnemyActions.Count == 0)
            throw new InvalidOperationException($"battle '{card.Id}' has no enemy actions");

        var candidates = card.EnemyActions.Where(a => IsTriggered(a, bs, card, players)).ToList();
        if (candidates.Count == 0)
            candidates = card.EnemyActions.Where(a => a.Trigger is AlwaysTrigger).ToList();
        if (candidates.Count == 0)
            candidates = card.EnemyActions.ToList();

        // Prefer higher-priority triggers: HpAtMost / PlayerHpAtMost / PreparedLastTurn / RoundAtLeast over Always.
        int Priority(EnemyActionTrigger t) => t switch
        {
            PreparedLastTurnTrigger => 5,
            HpAtMostTrigger => 4,
            PlayerHpAtMostTrigger => 3,
            RoundAtLeastTrigger => 2,
            AlwaysTrigger => 1,
            _ => 0
        };
        int maxPri = candidates.Max(a => Priority(a.Trigger));
        var top = candidates.Where(a => Priority(a.Trigger) == maxPri).ToList();

        var chosen = WeightedPick(top);

        int? target = NeedsTarget(chosen.Kind)
            ? SelectTarget(chosen.Targeting, players, bs.LastEnemyTargetIndex)
            : null;
        if (target is int ti) bs.LastEnemyTargetIndex = ti;

        string line = chosen.Kind switch
        {
            EnemyActionKind.Prepare => $"敵方動作：{chosen.Name} — 蓄勢。",
            EnemyActionKind.Defend => $"敵方動作：{chosen.Name} — 架起防禦。",
            EnemyActionKind.Flee => $"敵方動作：{chosen.Name} — 試圖逃跑。",
            EnemyActionKind.UseItem => target is int uIdx
                ? $"敵方動作：{chosen.Name} — 對 Player {uIdx + 1} 使用。"
                : $"敵方動作：{chosen.Name} — 施放。",
            EnemyActionKind.Attack => target is int aIdx
                ? $"敵方動作：{chosen.Name} — 鎖定 Player {aIdx + 1}。"
                : $"敵方動作：{chosen.Name}。",
            _ => $"敵方動作：{chosen.Name}。"
        };
        RecordLog(bs, line);

        return new EnemyActionPlan(chosen, target, line);
    }

    private EnemyAction WeightedPick(IReadOnlyList<EnemyAction> list)
    {
        if (list.Count == 1) return list[0];
        int totalWeight = list.Sum(a => Math.Max(1, a.Weight));
        int roll = _dice.RollD(totalWeight);
        int accum = 0;
        foreach (var a in list)
        {
            accum += Math.Max(1, a.Weight);
            if (roll <= accum) return a;
        }
        return list[^1];
    }

    private static bool IsTriggered(EnemyAction a, BattleState bs, BattleCard card, IReadOnlyList<PlayerState> players)
    {
        double enemyRatio = bs.EnemyHpMax == 0 ? 0 : (double)bs.EnemyHp / bs.EnemyHpMax;
        return a.Trigger switch
        {
            AlwaysTrigger => true,
            HpAtMostTrigger hp => enemyRatio <= hp.Ratio,
            PlayerHpAtMostTrigger php => players.Any(p => p.Hp > 0 && (double)p.Hp / Math.Max(1, p.HpMax) <= php.Ratio),
            RoundAtLeastTrigger r => bs.RoundNumber >= r.Round,
            PreparedLastTurnTrigger => bs.EnemyPrepared,
            _ => false
        };
    }

    private static bool NeedsTarget(EnemyActionKind kind)
        => kind is EnemyActionKind.Attack or EnemyActionKind.UseItem;

    private static int? SelectTarget(TargetingRule rule, IReadOnlyList<PlayerState> players, int? lastTarget = null)
    {
        var alive = new List<int>();
        for (int i = 0; i < players.Count; i++)
            if (players[i].Hp > 0) alive.Add(i);
        if (alive.Count == 0) return null;

        IEnumerable<int> ordered = rule switch
        {
            TargetingRule.LowestHp => alive.OrderBy(i => players[i].Hp),
            TargetingRule.HighestPower => alive.OrderByDescending(i => players[i].Hp),
            _ => alive
        };
        var list = ordered.ToList();
        if (list.Count > 1 && lastTarget is int prev && list[0] == prev)
        {
            int prevHp = players[prev].Hp;
            for (int i = 1; i < list.Count; i++)
                if (players[list[i]].Hp == prevHp) return list[i];
        }
        return list[0];
    }

    /// <summary>
    /// Execute the planned enemy action against the chosen player's response.
    /// Handles Attack/UseItem (damage + response branch), Prepare/Defend (status, no damage), Flee (ends battle).
    /// </summary>
    public EnemyActionResolution ResolveEnemyAction(
        BattleState bs, BattleCard card, EnemyActionPlan plan,
        PlayerResponseChoice response, IReadOnlyList<PlayerState> players,
        Module? module = null, GameState? gameState = null)
    {
        var action = plan.Action;
        bs.LastEnemyActionId = action.Id;
        int targetIdx = plan.TargetPlayerIndex ?? 0;

        switch (action.Kind)
        {
            case EnemyActionKind.Prepare:
                bs.EnemyPrepared = true;
                _log?.Append("敵方蓄勢完成，下次攻擊傷害加倍。", TurnLogKind.Miss);
                return Record(action, response, null, null, rawDmg: 0, dmgTaken: 0, reflected: 0,
                    targetIdx, dodged: false, $"敵方蓄勢完成，下次攻擊傷害加倍。");

            case EnemyActionKind.Defend:
                bs.EnemyPrepared = false;
                _log?.Append("敵方進入防禦姿態。", TurnLogKind.Neutral);
                return Record(action, response, null, null, 0, 0, 0, targetIdx, false,
                    "敵方進入防禦姿態。");

            case EnemyActionKind.Flee:
                bs.Phase = BattlePhase.EnemyFled;
                _log?.Append("敵方成功逃離戰鬥。", TurnLogKind.Neutral);
                return Record(action, response, null, null, 0, 0, 0, targetIdx, false,
                    "敵方成功逃離戰鬥。");

            case EnemyActionKind.Attack:
            case EnemyActionKind.UseItem:
                return ResolveHostile(bs, card, action, response, players, module, gameState, targetIdx);
        }

        throw new InvalidOperationException($"unsupported enemy action kind: {action.Kind}");
    }

    private EnemyActionResolution ResolveHostile(
        BattleState bs, BattleCard card, EnemyAction action, PlayerResponseChoice response,
        IReadOnlyList<PlayerState> players, Module? module, GameState? gameState, int targetIdx)
    {
        int baseDmg = card.Stats.AttackPower + action.Payload.Damage;
        if (bs.EnemyPrepared) { baseDmg *= 2; bs.EnemyPrepared = false; }

        var target = players[targetIdx];
        bool dodged = false;
        int dmgTaken = 0;
        int reflected = 0;
        RollResult? roll = null;
        int? total = null;
        string line;

        switch (response)
        {
            case AcceptResponse:
                dmgTaken = ApplyDamageToPlayer(bs, target, targetIdx, baseDmg);
                line = $"Player {targetIdx + 1} 選擇承受，受到 {dmgTaken} 傷害。";
                break;

            case DodgeResponse:
            {
                var character = ModuleCharacter(module, target);
                SpendResponseAp(target);
                roll = _dice.Roll2d6();
                int dodgeBonus = bs.PlayerDodgeBonusNext.GetValueOrDefault(targetIdx);
                bs.PlayerDodgeBonusNext[targetIdx] = 0;
                int evBonus = bs.PlayerEvasionBonusThisRound.GetValueOrDefault(targetIdx);
                int evasionTotal = roll.Value.Total + (character?.Stats.Skill ?? 0) + dodgeBonus + evBonus;
                total = evasionTotal;
                int atkValue = card.Stats.AttackPower;
                if (evasionTotal >= atkValue + 2) { dodged = true; line = $"Player {targetIdx + 1} 成功迴避（{evasionTotal} vs {atkValue}）。"; }
                else if (evasionTotal >= atkValue) { dmgTaken = ApplyDamageToPlayer(bs, target, targetIdx, Math.Max(1, baseDmg / 2)); line = $"Player {targetIdx + 1} 擦身而過（{evasionTotal} vs {atkValue}），受到 {dmgTaken} 傷害。"; }
                else { dmgTaken = ApplyDamageToPlayer(bs, target, targetIdx, baseDmg); line = $"Player {targetIdx + 1} 迴避失敗（{evasionTotal} vs {atkValue}），受到 {dmgTaken} 傷害。"; }
                break;
            }

            case BlockResponse:
            {
                var character = ModuleCharacter(module, target);
                SpendResponseAp(target);
                roll = _dice.Roll2d6();
                int shieldDef = module is null ? 0 : (EquipmentService.GetWeaponStats(target, module)?.HitBonus ?? 0);
                int blockTotal = roll.Value.Total + (character?.Stats.Power ?? 0) + shieldDef;
                total = blockTotal;
                int atkValue = card.Stats.AttackPower;
                if (blockTotal >= atkValue + 2) { dodged = true; line = $"Player {targetIdx + 1} 完全格擋（{blockTotal} vs {atkValue}）。"; }
                else if (blockTotal >= atkValue) { dmgTaken = ApplyDamageToPlayer(bs, target, targetIdx, Math.Max(1, baseDmg / 2)); line = $"Player {targetIdx + 1} 半擋（{blockTotal} vs {atkValue}），受到 {dmgTaken} 傷害。"; }
                else { dmgTaken = ApplyDamageToPlayer(bs, target, targetIdx, baseDmg); line = $"Player {targetIdx + 1} 格擋失敗（{blockTotal} vs {atkValue}），受到 {dmgTaken} 傷害。"; }
                break;
            }

            case CounterResponse:
            {
                var character = ModuleCharacter(module, target);
                SpendResponseAp(target);
                roll = _dice.Roll2d6();
                int counterTotal = roll.Value.Total + (character?.Stats.Power ?? 0);
                total = counterTotal;
                int atkValue = card.Stats.AttackPower;
                if (counterTotal >= atkValue)
                {
                    int counterDmg = Math.Max(1, (character?.Stats.Power ?? 0) - card.Stats.Defense);
                    bs.EnemyHp = Math.Max(0, bs.EnemyHp - counterDmg);
                    reflected = counterDmg;
                    dodged = true;
                    line = $"Player {targetIdx + 1} 反擊成功（{counterTotal} vs {atkValue}），對敵方造成 {counterDmg} 傷害。";
                }
                else
                {
                    dmgTaken = ApplyDamageToPlayer(bs, target, targetIdx, baseDmg);
                    line = $"Player {targetIdx + 1} 反擊失敗（{counterTotal} vs {atkValue}），受到 {dmgTaken} 傷害。";
                }
                break;
            }

            case ReflectResponse rr:
            {
                if (module is null) throw new InvalidOperationException("Reflect response requires module.");
                if (!module.ActionCards.TryGetValue(rr.CardId, out var reflectCard))
                    throw new InvalidOperationException($"reflect card '{rr.CardId}' not found");
                if (!target.Hand.Contains(rr.CardId))
                    throw new InvalidOperationException($"reflect card '{rr.CardId}' not in hand");
                if (reflectCard.Combat is null || !reflectCard.Combat.Tags.Any(t => string.Equals(t, "reflect", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"card '{rr.CardId}' is not a reflect card");

                target.Hand.Remove(rr.CardId);
                target.Discard.Add(rr.CardId);
                SpendResponseAp(target);

                int half = Math.Max(1, baseDmg / 2);
                dmgTaken = ApplyDamageToPlayer(bs, target, targetIdx, half);
                reflected = half;
                bs.EnemyHp = Math.Max(0, bs.EnemyHp - reflected);
                line = $"Player {targetIdx + 1} 使用「{reflectCard.Name}」反射：受 {dmgTaken}、反彈 {reflected}。";
                break;
            }

            default:
                throw new ArgumentException($"unknown response: {response}");
        }

        RecordLog(bs, line, dodged ? TurnLogKind.Hit : dmgTaken > 0 ? TurnLogKind.Miss : TurnLogKind.Neutral);
        return Record(action, response, roll, total, baseDmg, dmgTaken, reflected, targetIdx, dodged, line);
    }

    private static Character? ModuleCharacter(Module? module, PlayerState player)
    {
        if (module is null) return null;
        return module.Characters.TryGetValue(player.CharacterId, out var c) ? c : null;
    }

    private static void SpendResponseAp(PlayerState target)
    {
        if (target.ActionPoints > 0) target.ActionPoints -= 1;
    }

    private static int ApplyDamageToPlayer(BattleState bs, PlayerState target, int targetIdx, int raw)
    {
        int bonus = bs.PlayerNextHitBonusDamage.GetValueOrDefault(targetIdx);
        int dmg = Math.Max(1, raw + bonus);
        bs.PlayerNextHitBonusDamage[targetIdx] = 0;
        target.Hp = Math.Max(0, target.Hp - dmg);
        return dmg;
    }

    private static EnemyActionResolution Record(
        EnemyAction action, PlayerResponseChoice response, RollResult? roll, int? total,
        int rawDmg, int dmgTaken, int reflected, int targetIdx, bool dodged, string line)
        => new(action, response, roll, total, rawDmg, dmgTaken, reflected, targetIdx, dodged, line);

    // ─── End check ────────────────────────────────────────────────────

    public BattleEndResult CheckEnd(
        BattleState bs, BattleCard card, IReadOnlyList<PlayerState> players)
    {
        if (bs.Phase == BattlePhase.EnemyFled)
            return BattleEndResult.EnemyFled;
        if (bs.EnemyHp <= 0)
        {
            bs.Phase = BattlePhase.Victory;
            return BattleEndResult.Victory;
        }
        if (players.All(p => p.Hp <= 0))
        {
            bs.Phase = BattlePhase.Defeat;
            return BattleEndResult.Defeat;
        }
        return BattleEndResult.Ongoing;
    }
}
