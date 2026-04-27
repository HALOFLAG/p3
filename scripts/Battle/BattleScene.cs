// Phase 2 任務 13 Stage 4.5 — 戰鬥子場景（規格書 §1.8 + §4.5）。
// Stage 4.5：UI 重構為圖示版面（敵我立繪頂部 + HP bar 條 + STEP 卡片 + 反應選項列表 + 右側戰鬥日誌）。
// Phase 2 採 AcceptDialog overlay（modal）；OK 按鈕隱藏，戰鬥結束時自動關。
using System.Collections.Generic;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Battle;

public partial class BattleScene : AcceptDialog
{
    [Signal] public delegate void BattleClosedEventHandler();

    private const string PlayerPortraitPath = "res://art/portraits/人物A.png";
    private const string CompanionPortraitPath = "res://art/portraits/人物B.png";

    // 頂部立繪
    private TextureRect? _playerPortrait;
    private TextureRect? _companionPortrait;

    // HP 條 + 名字
    private Label? _heroNameLabel;
    private ProgressBar? _heroHpBar;
    private Label? _heroHpValueLabel;
    private Label? _heroApLabel;
    private Label? _companionNameLabel;
    private ProgressBar? _companionHpBar;
    private Label? _companionHpValueLabel;
    private Label? _enemyNameLabel;
    private ProgressBar? _enemyHpBar;
    private Label? _enemyHpValueLabel;
    private Label? _enemyDescriptionLabel;
    private Label? _enemyRevealNoteLabel;

    // STEP 1 先攻
    private PanelContainer? _initiativePlayerCell;
    private PanelContainer? _initiativeEnemyCell;
    private Label? _initiativePlayerLabel;
    private Label? _initiativeEnemyLabel;

    // STEP 2 怪物知識（5 屬性圓點 + 文字）
    private const int RevealDotCount = 5;
    private readonly ColorRect[] _revealDots = new ColorRect[RevealDotCount];
    private readonly Label[] _revealDotLabels = new Label[RevealDotCount];
    private static readonly string[] RevealAttributeNames = { "類型", "HP", "弱點", "抗性", "特徵" };

    // 主區 — 行動 / 反應
    private Label? _phaseLabel;
    private Label? _diceResultLabel;
    private VBoxContainer? _encounterPanel;
    private Button? _rollEncounterButton;
    private VBoxContainer? _playerTurnPanel;
    private Button? _basicAttackButton;
    private Button? _basicDefendButton;
    private Button? _basicObserveButton;
    private Button? _basicRepositionButton;
    private Button? _basicRetreatButton;
    private Button? _endPlayerTurnButton;
    private VBoxContainer? _enemyActionPanel;
    private Label? _enemyActionTitleLabel;
    private Label? _enemyActionDescLabel;
    private VBoxContainer? _responseListBox;
    private Button? _responseAcceptButton;
    private Button? _responseDodgeButton;
    private Button? _responseBlockButton;
    private Button? _responseCounterButton;
    private Button? _responseReflectButton;

    // 戰鬥日誌（右側）
    private Label? _logCurrentLabel;
    private RichTextLabel? _battleLogText;
    private readonly System.Collections.Generic.List<string> _battleLogLines = new();
    private const int MaxLogLines = 12;

    // 結算 + 結束戰鬥按鈕
    private Label? _resultLabel;
    private Button? _closeBattleButton;

    private BattleCard? _card;
    private BattleState? _state;
    private GameState? _gameState;
    private BattleEngine? _engine;
    private Character? _character;
    private Module? _module;
    private MessageBubbleService? _bubbles;
    private CardNarrative.Core.Services.EnemyActionPlan? _pendingEnemyPlan;
    private CompanionCombatSupport? _companionSupport;
    private Button? _supportAttackBoostButton;
    private Button? _supportRollSupportButton;
    private Button? _supportBlockDamageButton;
    /// <summary>Stage 6 — 確保 LootEffects / 重生邏輯只跑一次（AdvanceBattleFlow 可能多次呼到 Victory case）。</summary>
    private bool _battleEndResolved;

    public override void _Ready()
    {
        Title = "戰鬥";
        Size = new Vector2I(1100, 900);
        Borderless = false;
        Theme = UiTheme.Build();
        // 隱藏 AcceptDialog 內建 OK 按鈕（圖中設計沒有；改用內部「結束戰鬥」按鈕在戰鬥結束時顯示）
        var okBtn = GetOkButton();
        if (okBtn != null) okBtn.Visible = false;

        Confirmed += () => EmitSignal(SignalName.BattleClosed);
        Canceled += () => EmitSignal(SignalName.BattleClosed);

        BuildLayout();
    }

    public void OpenWithBattle(
        BattleEngine engine,
        BattleCard card,
        BattleState bs,
        GameState state,
        Character character,
        Module module,
        MessageBubbleService? bubbles = null)
    {
        _engine = engine;
        _card = card;
        _state = bs;
        _gameState = state;
        _character = character;
        _module = module;
        _bubbles = bubbles;
        _companionSupport = new CompanionCombatSupport();
        // 戰鬥開始重置同伴輔助冷卻（每戰 1 次）+ 清除蓄勢殘留
        foreach (var c in state.Companions) c.UsedCombatSupportThisBattle.Clear();
        bs.CompanionBlockPending = false;
        bs.CompanionBlockSourceIdx = -1;
        _battleEndResolved = false;

        _battleLogLines.Clear();
        UpdateAll();
        AppendLog($"戰鬥開始：{card.Description}");

        if (_diceResultLabel != null) _diceResultLabel.Text = "（尚未擲骰）";
        if (_resultLabel != null) _resultLabel.Text = "";
        if (_rollEncounterButton != null) _rollEncounterButton.Disabled = false;
        if (_closeBattleButton != null) _closeBattleButton.Visible = false;

        PopupCentered();
    }

    /// <summary>統一更新所有 UI 區塊（OpenWithBattle / 各 action 後呼）。</summary>
    private void UpdateAll()
    {
        UpdateHeroStatus();
        UpdateCompanionStatus();
        UpdateEnemyStatus();
        UpdatePhaseLabel();
        UpdateInitiative();
        UpdateRevealDots();
        UpdatePlayerTurnButtons();
        UpdateResponseButtons();
        UpdateActionPanelVisibility();
    }

    private void AppendLog(string line)
    {
        _battleLogLines.Add(line);
        while (_battleLogLines.Count > MaxLogLines) _battleLogLines.RemoveAt(0);
        if (_logCurrentLabel != null) _logCurrentLabel.Text = line;
        if (_battleLogText != null)
        {
            _battleLogText.Clear();
            foreach (var l in _battleLogLines) _battleLogText.AppendText(l + "\n");
        }
    }

    // ─── 行動 handlers ─────────────────────────────────────────

    private void OnRollEncounterPressed()
    {
        if (_engine is null || _card is null || _state is null || _gameState is null || _character is null) return;
        if (_state.Phase != BattlePhase.Encounter) return;

        var resolution = _engine.ResolveEncounter(_state, _card, _gameState.CurrentPlayer, _character, _module);
        if (_diceResultLabel != null)
            _diceResultLabel.Text = $"擲骰 {resolution.Roll.D1}+{resolution.Roll.D2} = {resolution.Total} vs TN {resolution.Tn} → {TierLabel(resolution.Tier)}";
        AppendLog(resolution.LogLine);

        if (_rollEncounterButton != null) _rollEncounterButton.Disabled = true;
        UpdateAll();
        AdvanceBattleFlow();
    }

    private void OnBasicActionPressed(BasicActionKind kind)
    {
        if (_engine is null || _card is null || _state is null || _gameState is null || _character is null) return;
        if (_state.Phase != BattlePhase.PlayerTurn) return;
        if (_state.UsedBasicActionThisTurn.Contains(_state.ActivePlayerIndex))
        {
            AppendLog("（本回合 Basic 行動已用過）");
            return;
        }

        var resolution = _engine.ResolvePlayerAction(
            _state, _card, _gameState.CurrentPlayer, _character,
            new BasicActionChoice(kind), _gameState, _module);
        AppendLog(resolution.LogLine);
        UpdateAll();
        AdvanceBattleFlow();
    }

    /// <summary>Stage 5 — 玩家點同伴輔助 entry：呼對應 service method 設置 BattleState 旗標。</summary>
    private void OnCompanionSupportPressed(CompanionCombatSupportKind kind)
    {
        if (_companionSupport is null || _state is null || _gameState is null) return;
        if (_state.Phase != BattlePhase.PlayerTurn) return;
        if (_gameState.Companions.Count == 0) return;
        const int companionIdx = 0; // Phase 2 只有 1 同伴

        bool ok = kind switch
        {
            CompanionCombatSupportKind.AttackBoost => _companionSupport.TryAttackBoost(_state, _gameState, companionIdx),
            CompanionCombatSupportKind.RollSupport => _companionSupport.TryRollSupport(_state, _gameState, companionIdx),
            CompanionCombatSupportKind.BlockDamage => _companionSupport.TryBlockDamage(_state, _gameState, companionIdx),
            _ => false,
        };

        AppendLog(ok
            ? $"同伴啟動「{CompanionSupportLabel(kind)}」。"
            : $"（同伴輔助「{CompanionSupportLabel(kind)}」無法使用 — 已用過或同伴失能）");
        UpdateAll();
    }

    private static string CompanionSupportLabel(CompanionCombatSupportKind kind) => kind switch
    {
        CompanionCombatSupportKind.AttackBoost => "攻擊加乘",
        CompanionCombatSupportKind.RollSupport => "行動輔助",
        CompanionCombatSupportKind.BlockDamage => "抵擋傷害",
        _ => kind.ToString(),
    };

    private void OnEndPlayerTurnPressed()
    {
        if (_state is null || _state.Phase != BattlePhase.PlayerTurn) return;
        _state.Phase = BattlePhase.EnemyTurn;
        AppendLog("（玩家結束本回合）");
        UpdateAll();
        AdvanceBattleFlow();
    }

    private void AdvanceBattleFlow()
    {
        if (_engine is null || _card is null || _state is null || _gameState is null) return;
        _engine.CheckEnd(_state, _card, _gameState.Players);

        switch (_state.Phase)
        {
            case BattlePhase.EnemyTurn:
                _pendingEnemyPlan = _engine.PlanEnemyAction(_state, _card, _gameState.Players);
                AppendLog(_pendingEnemyPlan.LogLine);
                if (_pendingEnemyPlan.Action.Kind == EnemyActionKind.Attack
                    || (_pendingEnemyPlan.Action.Kind == EnemyActionKind.UseItem
                        && _pendingEnemyPlan.Action.Payload.Damage > 0))
                {
                    // Stage 5：若同伴蓄勢中，直接觸發 short-circuit 不顯示 Response Dialog
                    if (_state.CompanionBlockPending)
                    {
                        var blockResolution = _engine.ResolveEnemyAction(
                            _state, _card, _pendingEnemyPlan, new AcceptResponse(),
                            _gameState.Players, _module, _gameState);
                        AppendLog(blockResolution.LogLine);
                        _pendingEnemyPlan = null;
                        EndEnemyTurnAndReset();
                        break;
                    }
                    _state.Phase = BattlePhase.AwaitingResponse;
                    _state.PendingEnemyAction = _pendingEnemyPlan.Action;
                    _state.PendingResponseTargetPlayerIndex = _pendingEnemyPlan.TargetPlayerIndex;
                    if (_enemyActionTitleLabel != null) _enemyActionTitleLabel.Text = $"敵方行動 · {_pendingEnemyPlan.Action.Name}";
                    if (_enemyActionDescLabel != null)
                        _enemyActionDescLabel.Text = $"敵方對 Player {(_pendingEnemyPlan.TargetPlayerIndex ?? 0) + 1} 發動攻擊。";
                    UpdateAll();
                }
                else
                {
                    var resolution = _engine.ResolveEnemyAction(
                        _state, _card, _pendingEnemyPlan, new AcceptResponse(),
                        _gameState.Players, _module, _gameState);
                    AppendLog(resolution.LogLine);
                    _pendingEnemyPlan = null;
                    EndEnemyTurnAndReset();
                }
                break;

            case BattlePhase.Victory:
            case BattlePhase.Defeat:
            case BattlePhase.EnemyFled:
                ApplyBattleEndResolution(_state.Phase);
                if (_resultLabel != null)
                    _resultLabel.Text = _state.Phase switch
                    {
                        BattlePhase.Victory => "戰鬥結束：勝利",
                        BattlePhase.Defeat => "戰鬥結束：失敗（緊急復活 HP=1）",
                        _ => "戰鬥結束：敵方逃離",
                    };
                if (_closeBattleButton != null) _closeBattleButton.Visible = true;
                UpdateAll();
                break;
        }
    }

    /// <summary>
    /// Stage 6 — 戰鬥結束結算（Victory/Defeat/EnemyFled）。idempotent：用 _battleEndResolved 守衛只跑一次。
    /// Victory 套用 BattleCard.LootEffects（規格 §1.8 + §1.4）；Defeat 簡化重生 hp=1（§1.10 完整重生留 Phase 3）；
    /// EnemyFled 不套 LootEffects（撤退無戰利品）。所有結算訊息走 MessageBubbleService（戰鬥外訊息）。
    /// </summary>
    private void ApplyBattleEndResolution(BattlePhase endPhase)
    {
        if (_battleEndResolved) return;
        if (_card is null || _gameState is null) return;
        _battleEndResolved = true;

        switch (endPhase)
        {
            case BattlePhase.Victory:
            {
                var handler = new EffectHandler();
                var lootSummary = new System.Collections.Generic.List<string>();
                foreach (var effect in _card.LootEffects)
                {
                    handler.Apply(effect, _gameState, _module);
                    lootSummary.Add(SummarizeLootEffect(effect));
                }
                string lootText = lootSummary.Count > 0 ? string.Join("、", lootSummary) : "無戰利品";
                AppendLog($"獲得戰利品：{lootText}");
                _bubbles?.Push(
                    text: $"擊敗「{_card.Name}」 → 戰利品：{lootText}",
                    source: MessageBubbleSource.OrbitSlot,
                    sourceId: _card.Id,
                    timestamp: System.DateTime.UtcNow,
                    isImportant: true);
                break;
            }
            case BattlePhase.Defeat:
            {
                _gameState.CurrentPlayer.Hp = 1;
                AppendLog("緊急復活：玩家 HP = 1（規格 §1.10 完整重生待 Phase 3 任務 14）");
                _bubbles?.Push(
                    text: "戰鬥失敗 — 緊急復活（HP=1）",
                    source: MessageBubbleSource.SystemHint,
                    sourceId: _card.Id,
                    timestamp: System.DateTime.UtcNow,
                    isImportant: true);
                break;
            }
            case BattlePhase.EnemyFled:
            {
                AppendLog("撤退完成，本場戰鬥不獲得戰利品");
                _bubbles?.Push(
                    text: "逃離戰鬥（無戰利品）",
                    source: MessageBubbleSource.SystemHint,
                    sourceId: _card.Id,
                    timestamp: System.DateTime.UtcNow,
                    isImportant: false);
                break;
            }
        }
    }

    private static string SummarizeLootEffect(EffectBase effect) => effect switch
    {
        GrantEquipmentEffect ge => $"裝備「{ge.Id}」",
        GrantResourceEffect gr => $"{gr.Key} +{gr.Amount}",
        SetFlagEffect sf => $"設旗「{sf.Key}」",
        _ => effect.GetType().Name,
    };

    private void OnResponsePressed(PlayerResponseChoice response)
    {
        if (_engine is null || _card is null || _state is null || _gameState is null) return;
        if (_state.Phase != BattlePhase.AwaitingResponse || _pendingEnemyPlan is null) return;

        var resolution = _engine.ResolveEnemyAction(
            _state, _card, _pendingEnemyPlan, response, _gameState.Players, _module, _gameState);
        AppendLog(resolution.LogLine);
        _pendingEnemyPlan = null;

        if (_state.Phase == BattlePhase.AwaitingResponse) _state.Phase = BattlePhase.PlayerTurn;
        EndEnemyTurnAndReset();
    }

    private void EndEnemyTurnAndReset()
    {
        if (_state is null || _gameState is null) return;
        if (_state.Phase is BattlePhase.Victory or BattlePhase.Defeat or BattlePhase.EnemyFled)
        {
            UpdateAll();
            AdvanceBattleFlow();
            return;
        }
        _state.Phase = BattlePhase.PlayerTurn;
        _state.RoundNumber++;
        _state.UsedBasicActionThisTurn.Remove(_state.ActivePlayerIndex);
        int penalty = _state.PendingApPenalty.GetValueOrDefault(_state.ActivePlayerIndex, 0);
        _gameState.CurrentPlayer.ActionPoints = 3 - penalty;
        if (penalty > 0) _state.PendingApPenalty[_state.ActivePlayerIndex] = 0;
        AppendLog($"進入第 {_state.RoundNumber} 回合（玩家 AP {_gameState.CurrentPlayer.ActionPoints}/3）");
        UpdateAll();
    }

    // ─── 區塊 update 方法 ───────────────────────────────────────

    private void UpdateHeroStatus()
    {
        if (_gameState is null) return;
        var p = _gameState.CurrentPlayer;
        if (_heroNameLabel != null && _character != null) _heroNameLabel.Text = _character.Name;
        if (_heroHpBar != null) { _heroHpBar.MaxValue = p.HpMax; _heroHpBar.Value = p.Hp; }
        if (_heroHpValueLabel != null) _heroHpValueLabel.Text = $"{p.Hp} / {p.HpMax}";
        if (_heroApLabel != null) _heroApLabel.Text = $"AP {p.ActionPoints}/3";
    }

    private void UpdateCompanionStatus()
    {
        if (_gameState is null || _module is null) return;
        if (_gameState.Companions.Count == 0)
        {
            if (_companionNameLabel != null) _companionNameLabel.Text = "（無同伴）";
            if (_companionHpBar != null) _companionHpBar.Value = 0;
            if (_companionHpValueLabel != null) _companionHpValueLabel.Text = "";
            if (_companionPortrait != null) _companionPortrait.Visible = false;
            return;
        }
        var cs = _gameState.Companions[0];
        var companion = _module.NpcCompanions.GetValueOrDefault(cs.CompanionId);
        if (_companionNameLabel != null) _companionNameLabel.Text = companion?.Name ?? cs.CompanionId;
        if (_companionHpBar != null && companion != null)
        {
            _companionHpBar.MaxValue = companion.Hp;
            _companionHpBar.Value = cs.Hp;
        }
        if (_companionHpValueLabel != null && companion != null)
            _companionHpValueLabel.Text = $"{cs.Hp} / {companion.Hp}";
        if (_companionPortrait != null) _companionPortrait.Visible = true;
    }

    private void UpdateEnemyStatus()
    {
        if (_state is null || _card is null) return;
        if (_enemyNameLabel != null) _enemyNameLabel.Text = _card.Name;
        if (_enemyHpBar != null) { _enemyHpBar.MaxValue = _state.EnemyHpMax; _enemyHpBar.Value = _state.EnemyHp; }
        if (_enemyHpValueLabel != null) _enemyHpValueLabel.Text = $"{_state.EnemyHp} / {_state.EnemyHpMax}";
        if (_enemyDescriptionLabel != null) _enemyDescriptionLabel.Text = _card.Description;
        if (_enemyRevealNoteLabel != null)
            _enemyRevealNoteLabel.Text = _state.Reveal switch
            {
                RevealLevel.Full => "情報完整揭露",
                RevealLevel.Partial => "情報部分揭露",
                RevealLevel.None => "情報不明",
                _ => "—",
            };
    }

    private void UpdatePhaseLabel()
    {
        if (_phaseLabel == null || _state == null) return;
        _phaseLabel.Text = $"階段：{PhaseLabel(_state.Phase)}";
    }

    private void UpdateInitiative()
    {
        if (_state is null) return;
        // 紅邊框 = 當前先攻方；初始 (Phase=Encounter / Start) 時兩格都不亮
        bool resolved = _state.Phase != BattlePhase.Start && _state.Phase != BattlePhase.Encounter;
        bool playerFirst = resolved && _state.PlayerGoesFirst;
        bool enemyFirst = resolved && !_state.PlayerGoesFirst;
        if (_initiativePlayerCell != null) ApplyInitiativeCellStyle(_initiativePlayerCell, playerFirst);
        if (_initiativeEnemyCell != null) ApplyInitiativeCellStyle(_initiativeEnemyCell, enemyFirst);
        // Tier 文字（優勢 / 正常 / 劣勢被襲）描述的是我方狀態，永遠顯示在我方格；敵方格永遠 "—"
        if (_initiativePlayerLabel != null)
            _initiativePlayerLabel.Text = resolved ? TierShort(_state.Tier) : "—";
        if (_initiativeEnemyLabel != null)
            _initiativeEnemyLabel.Text = "—";
    }

    private static void ApplyInitiativeCellStyle(PanelContainer cell, bool active)
    {
        var sb = new StyleBoxFlat
        {
            BgColor = Palette.PaperLight,
            BorderColor = active ? Palette.RedDark : Palette.InkLight,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        sb.BorderWidthLeft = sb.BorderWidthRight = sb.BorderWidthTop = sb.BorderWidthBottom = active ? 2 : 1;
        cell.AddThemeStyleboxOverride("panel", sb);
    }

    private void UpdateRevealDots()
    {
        if (_state is null) return;
        // None=0 個亮、Partial=2 個亮（類型/HP）、Full=5 個全亮
        int litCount = _state.Reveal switch { RevealLevel.Full => 5, RevealLevel.Partial => 2, _ => 0 };
        for (int i = 0; i < RevealDotCount; i++)
        {
            if (_revealDots[i] == null) continue;
            _revealDots[i].Color = i < litCount ? Palette.Green : Palette.Brown;
        }
    }

    private void UpdatePlayerTurnButtons()
    {
        bool isPlayerTurn = _state is not null && _state.Phase == BattlePhase.PlayerTurn;
        bool basicUsed = _state is not null && _state.UsedBasicActionThisTurn.Contains(_state.ActivePlayerIndex);
        bool enabled = isPlayerTurn && !basicUsed;
        if (_basicAttackButton != null) _basicAttackButton.Disabled = !enabled;
        if (_basicDefendButton != null) _basicDefendButton.Disabled = !enabled;
        if (_basicObserveButton != null) _basicObserveButton.Disabled = !enabled;
        if (_basicRepositionButton != null) _basicRepositionButton.Disabled = !enabled;
        if (_basicRetreatButton != null) _basicRetreatButton.Disabled = !isPlayerTurn;
        if (_endPlayerTurnButton != null) _endPlayerTurnButton.Disabled = !isPlayerTurn;

        // Stage 5：同伴輔助按鈕 — Phase=PlayerTurn + 該種輔助本戰未用過 + 同伴 HP>0
        bool hasCompanion = _gameState is not null && _gameState.Companions.Count > 0;
        if (_supportAttackBoostButton != null)
            _supportAttackBoostButton.Disabled = !isPlayerTurn || !hasCompanion
                || (_gameState != null && !CompanionCombatSupport.CanUseSupport(_gameState, 0, CompanionCombatSupportKind.AttackBoost));
        if (_supportRollSupportButton != null)
            _supportRollSupportButton.Disabled = !isPlayerTurn || !hasCompanion
                || (_gameState != null && !CompanionCombatSupport.CanUseSupport(_gameState, 0, CompanionCombatSupportKind.RollSupport));
        if (_supportBlockDamageButton != null)
            _supportBlockDamageButton.Disabled = !isPlayerTurn || !hasCompanion
                || (_gameState != null && !CompanionCombatSupport.CanUseSupport(_gameState, 0, CompanionCombatSupportKind.BlockDamage));
    }

    private void UpdateResponseButtons()
    {
        if (_state is null || _gameState is null) return;
        bool isResp = _state.Phase == BattlePhase.AwaitingResponse;
        int ap = _gameState.CurrentPlayer.ActionPoints;
        if (_responseAcceptButton != null) _responseAcceptButton.Disabled = !isResp;
        if (_responseDodgeButton != null) _responseDodgeButton.Disabled = !isResp || ap < 1;
        if (_responseBlockButton != null) _responseBlockButton.Disabled = !isResp || ap < 1;
        if (_responseCounterButton != null) _responseCounterButton.Disabled = !isResp || ap < 1;
        if (_responseReflectButton != null) _responseReflectButton.Disabled = true; // Phase 2 無反射卡資料
    }

    private void UpdateActionPanelVisibility()
    {
        if (_state is null) return;
        if (_encounterPanel != null) _encounterPanel.Visible = _state.Phase == BattlePhase.Encounter;
        if (_playerTurnPanel != null) _playerTurnPanel.Visible = _state.Phase == BattlePhase.PlayerTurn;
        if (_enemyActionPanel != null) _enemyActionPanel.Visible = _state.Phase == BattlePhase.AwaitingResponse;
    }

    // ─── BuildLayout 重構（圖示版面）─────────────────────────────

    private void BuildLayout()
    {
        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddThemeConstantOverride("separation", 6);
        AddChild(root);

        // === 1. 頂部立繪區（HBox：左玩家+同伴 / 右敵人 placeholder）===
        var portraitsRow = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 100),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        portraitsRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(portraitsRow);

        // 我方立繪（玩家 + 同伴 並排，置中於左半邊）
        var heroCenter = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        portraitsRow.AddChild(heroCenter);
        var heroPortraitsBox = new HBoxContainer();
        heroPortraitsBox.AddThemeConstantOverride("separation", 8);
        heroCenter.AddChild(heroPortraitsBox);
        _playerPortrait = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(PlayerPortraitPath),
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(70, 100),
        };
        heroPortraitsBox.AddChild(_playerPortrait);
        _companionPortrait = new TextureRect
        {
            Texture = ResourceLoader.Load<Texture2D>(CompanionPortraitPath),
            ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(70, 100),
        };
        heroPortraitsBox.AddChild(_companionPortrait);

        // 敵方立繪 placeholder（圖中為紫色矩形 ×3，置中於右半邊）
        var enemyCenter = new CenterContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        portraitsRow.AddChild(enemyCenter);
        var enemyPortraitsBox = new HBoxContainer();
        enemyPortraitsBox.AddThemeConstantOverride("separation", 8);
        enemyCenter.AddChild(enemyPortraitsBox);
        for (int i = 0; i < 3; i++)
        {
            var placeholder = new ColorRect
            {
                Color = Palette.Purple,
                CustomMinimumSize = new Vector2(45, 90),
            };
            enemyPortraitsBox.AddChild(placeholder);
        }

        // 黑色分隔線
        var sep = new ColorRect { Color = Palette.Ink, CustomMinimumSize = new Vector2(0, 2) };
        root.AddChild(sep);

        // === 2. HP 條中部區（HBox 兩欄）===
        var hpRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        hpRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(hpRow);

        // 我方 HP block
        var heroHpBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        heroHpBox.AddThemeConstantOverride("separation", 2);
        var heroSectionLabel = MakeLabel("我方隊伍", 10, false);
        heroSectionLabel.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        heroHpBox.AddChild(heroSectionLabel);
        var heroHpRow = new HBoxContainer();
        heroHpRow.AddThemeConstantOverride("separation", 6);
        _heroNameLabel = MakeLabel("艾絲黛", 12, true);
        _heroNameLabel.CustomMinimumSize = new Vector2(80, 0);
        heroHpRow.AddChild(_heroNameLabel);
        _heroHpBar = MakeHpBar(Palette.Green);
        heroHpRow.AddChild(_heroHpBar);
        _heroHpValueLabel = MakeLabel("— / —", 11, false);
        _heroHpValueLabel.CustomMinimumSize = new Vector2(60, 0);
        heroHpRow.AddChild(_heroHpValueLabel);
        _heroApLabel = MakeLabel("AP —/3", 11, false);
        heroHpRow.AddChild(_heroApLabel);
        heroHpBox.AddChild(heroHpRow);
        // 同伴 HP（緊接玩家下方）
        var compHpRow = new HBoxContainer();
        compHpRow.AddThemeConstantOverride("separation", 6);
        _companionNameLabel = MakeLabel("（同伴）", 11, false);
        _companionNameLabel.CustomMinimumSize = new Vector2(80, 0);
        compHpRow.AddChild(_companionNameLabel);
        _companionHpBar = MakeHpBar(Palette.Brown);
        compHpRow.AddChild(_companionHpBar);
        _companionHpValueLabel = MakeLabel("— / —", 11, false);
        _companionHpValueLabel.CustomMinimumSize = new Vector2(60, 0);
        compHpRow.AddChild(_companionHpValueLabel);
        heroHpBox.AddChild(compHpRow);
        hpRow.AddChild(heroHpBox);

        // 敵方 HP block
        var enemyHpBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        enemyHpBox.AddThemeConstantOverride("separation", 2);
        var enemySectionLabel = MakeLabel("敵方", 10, false);
        enemySectionLabel.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        enemyHpBox.AddChild(enemySectionLabel);
        var enemyHpRow = new HBoxContainer();
        enemyHpRow.AddThemeConstantOverride("separation", 6);
        _enemyNameLabel = MakeLabel("（敵人）", 12, true);
        _enemyNameLabel.CustomMinimumSize = new Vector2(100, 0);
        enemyHpRow.AddChild(_enemyNameLabel);
        _enemyHpBar = MakeHpBar(Palette.Green);
        enemyHpRow.AddChild(_enemyHpBar);
        _enemyHpValueLabel = MakeLabel("— / —", 11, false);
        _enemyHpValueLabel.CustomMinimumSize = new Vector2(60, 0);
        enemyHpRow.AddChild(_enemyHpValueLabel);
        enemyHpBox.AddChild(enemyHpRow);
        _enemyDescriptionLabel = MakeLabel("", 10, false);
        _enemyDescriptionLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        enemyHpBox.AddChild(_enemyDescriptionLabel);
        _enemyRevealNoteLabel = MakeLabel("", 10, false);
        _enemyRevealNoteLabel.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        enemyHpBox.AddChild(_enemyRevealNoteLabel);
        hpRow.AddChild(enemyHpBox);

        // === 3. STEP 卡片區（HBox 兩欄）===
        var stepRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        stepRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(stepRow);

        // STEP 1 · 先攻
        var step1Box = MakeSection("STEP 1 · 先攻");
        step1Box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var initRow = new HBoxContainer();
        initRow.AddThemeConstantOverride("separation", 8);
        _initiativePlayerCell = new PanelContainer { CustomMinimumSize = new Vector2(0, 50), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var initPlayerVBox = new VBoxContainer();
        initPlayerVBox.AddChild(MakeLabel("我方", 10, false));
        _initiativePlayerLabel = MakeLabel("—", 12, true);
        _initiativePlayerLabel.HorizontalAlignment = HorizontalAlignment.Center;
        initPlayerVBox.AddChild(_initiativePlayerLabel);
        _initiativePlayerCell.AddChild(initPlayerVBox);
        ApplyInitiativeCellStyle(_initiativePlayerCell, false);
        initRow.AddChild(_initiativePlayerCell);

        _initiativeEnemyCell = new PanelContainer { CustomMinimumSize = new Vector2(0, 50), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var initEnemyVBox = new VBoxContainer();
        initEnemyVBox.AddChild(MakeLabel("敵方", 10, false));
        _initiativeEnemyLabel = MakeLabel("—", 12, true);
        _initiativeEnemyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        initEnemyVBox.AddChild(_initiativeEnemyLabel);
        _initiativeEnemyCell.AddChild(initEnemyVBox);
        ApplyInitiativeCellStyle(_initiativeEnemyCell, false);
        initRow.AddChild(_initiativeEnemyCell);
        step1Box.AddChild(initRow);
        stepRow.AddChild(step1Box);

        // STEP 2 · 怪物知識（5 圓點）
        var step2Box = MakeSection("STEP 2 · 怪物知識");
        step2Box.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var dotsRow = new HBoxContainer();
        dotsRow.AddThemeConstantOverride("separation", 12);
        for (int i = 0; i < RevealDotCount; i++)
        {
            var dotVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            dotVBox.AddThemeConstantOverride("separation", 2);
            var dot = new ColorRect
            {
                Color = Palette.Brown,
                CustomMinimumSize = new Vector2(12, 12),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            _revealDots[i] = dot;
            dotVBox.AddChild(dot);
            var lbl = MakeLabel(RevealAttributeNames[i], 10, false);
            lbl.HorizontalAlignment = HorizontalAlignment.Center;
            _revealDotLabels[i] = lbl;
            dotVBox.AddChild(lbl);
            dotsRow.AddChild(dotVBox);
        }
        step2Box.AddChild(dotsRow);
        stepRow.AddChild(step2Box);

        // === 4. 主區（HBox 左右分：行動 / 戰鬥日誌）===
        var mainRow = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        mainRow.AddThemeConstantOverride("separation", 12);
        root.AddChild(mainRow);

        // 左側：行動區包 ScrollContainer（PlayerTurn panel 縱向長 — 加同伴輔助後易溢出）
        // 重點：ScrollContainer 自身 ExpandFill 填滿父容器，但 child leftBox 不能 ExpandFill，
        // 否則 child 試圖等於 ScrollContainer 大小 → 永遠不溢出 → 沒滾動條。
        var leftScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 2.0f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            // 給 ScrollContainer 明確最小高度，確保 child 內容超過時觸發 scroll（避免 HBox 把 ScrollContainer 拉伸到 child 自然高度）
            CustomMinimumSize = new Vector2(0, 360),
        };
        mainRow.AddChild(leftScroll);
        var leftBox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            // 不設 SizeFlagsVertical（用預設 Fill 不 expand）→ 高度由內容決定 → 超過 ScrollContainer 才會出現 scroll
        };
        leftBox.AddThemeConstantOverride("separation", 6);
        leftScroll.AddChild(leftBox);

        _phaseLabel = MakeLabel("階段：—", 11, true);
        leftBox.AddChild(_phaseLabel);

        // Encounter sub-panel（Phase=Encounter 顯示）
        _encounterPanel = MakeSection("【遭遇判定 Phase A】");
        _rollEncounterButton = new Button { Text = "擲骰判定" };
        _rollEncounterButton.AddThemeFontSizeOverride("font_size", 12);
        UiTheme.ApplyPrimaryButtonStyle(_rollEncounterButton);
        _rollEncounterButton.Pressed += OnRollEncounterPressed;
        _encounterPanel.AddChild(_rollEncounterButton);
        _diceResultLabel = MakeLabel("（尚未擲骰）", 11, false);
        _diceResultLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        _encounterPanel.AddChild(_diceResultLabel);
        leftBox.AddChild(_encounterPanel);

        // PlayerTurn sub-panel — 改為圖中列表樣式（基本行動 5 項 + 角色行動 / 動作卡 placeholder section）
        _playerTurnPanel = new VBoxContainer();
        _playerTurnPanel.AddThemeConstantOverride("separation", 4);
        // Section 1：基本行動（每回合 1 次，免費）
        var basicSectionLabel = MakeLabel("基本行動（每回合 1 次，免費）", 11, false);
        basicSectionLabel.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        _playerTurnPanel.AddChild(basicSectionLabel);
        var basicListBox = new VBoxContainer();
        basicListBox.AddThemeConstantOverride("separation", 3);
        _basicAttackButton = MakeListEntry(basicListBox, "基本攻擊", "免費",
            "2d6+Power vs 敵方閃避，命中造成傷害", "使用",
            () => OnBasicActionPressed(BasicActionKind.Attack));
        _basicDefendButton = MakeListEntry(basicListBox, "防禦", "免費",
            "本回合閃避 +2", "使用",
            () => OnBasicActionPressed(BasicActionKind.Defend));
        _basicObserveButton = MakeListEntry(basicListBox, "觀察", "免費",
            "重擲遭遇判定提升情報", "使用",
            () => OnBasicActionPressed(BasicActionKind.Observe));
        _basicRepositionButton = MakeListEntry(basicListBox, "重整位置", "免費",
            "下次迴避 +2", "使用",
            () => OnBasicActionPressed(BasicActionKind.Reposition));
        _basicRetreatButton = MakeListEntry(basicListBox, "撤退", "免費",
            "結束戰鬥（不獲得獎勵）", "使用",
            () => OnBasicActionPressed(BasicActionKind.Retreat));
        _playerTurnPanel.AddChild(basicListBox);

        // 角色行動 / 動作卡 section 留 Phase 3 任務 14 接 Character Ability + ActionCard 時補

        // Section 2：同伴輔助（每戰 1 次） — Stage 5
        var supportSectionLabel = MakeLabel("同伴輔助（每戰 1 次）", 11, false);
        supportSectionLabel.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        _playerTurnPanel.AddChild(supportSectionLabel);
        var supportListBox = new VBoxContainer();
        supportListBox.AddThemeConstantOverride("separation", 3);
        _supportAttackBoostButton = MakeListEntry(supportListBox, "攻擊加乘", "同伴 1 AP",
            "玩家下次攻擊命中時傷害 +2", "使用",
            () => OnCompanionSupportPressed(CompanionCombatSupportKind.AttackBoost));
        _supportRollSupportButton = MakeListEntry(supportListBox, "行動輔助", "同伴 1 AP",
            "玩家下次擲骰 +2（攻擊 / 迴避 / 反擊）", "使用",
            () => OnCompanionSupportPressed(CompanionCombatSupportKind.RollSupport));
        _supportBlockDamageButton = MakeListEntry(supportListBox, "抵擋傷害（蓄勢）", "同伴 1 AP",
            "玩家下次受擊由同伴代受全額傷害", "使用",
            () => OnCompanionSupportPressed(CompanionCombatSupportKind.BlockDamage));
        _playerTurnPanel.AddChild(supportListBox);

        // 結束本回合按鈕（圖中無，但 Phase 2 簡化需提供手動切 Enemy Turn 的方式）
        _endPlayerTurnButton = new Button { Text = "結束本回合", SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
        _endPlayerTurnButton.CustomMinimumSize = new Vector2(120, 0);
        _endPlayerTurnButton.AddThemeFontSizeOverride("font_size", 11);
        _endPlayerTurnButton.Pressed += OnEndPlayerTurnPressed;
        _playerTurnPanel.AddChild(_endPlayerTurnButton);

        leftBox.AddChild(_playerTurnPanel);

        // EnemyAction + Response sub-panel
        _enemyActionPanel = new VBoxContainer { Visible = false };
        _enemyActionPanel.AddThemeConstantOverride("separation", 4);
        var enemyActionHeader = new PanelContainer();
        var enemyActionHeaderStyle = new StyleBoxFlat
        {
            BgColor = Palette.WithAlpha(Palette.Red, 0.08f),
            BorderColor = Palette.RedDark,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        enemyActionHeaderStyle.BorderWidthLeft = enemyActionHeaderStyle.BorderWidthRight =
            enemyActionHeaderStyle.BorderWidthTop = enemyActionHeaderStyle.BorderWidthBottom = 1;
        enemyActionHeader.AddThemeStyleboxOverride("panel", enemyActionHeaderStyle);
        var enemyActionVBox = new VBoxContainer();
        _enemyActionTitleLabel = MakeLabel("敵方行動", 12, true);
        _enemyActionTitleLabel.AddThemeColorOverride("font_color", Palette.RedDark);
        enemyActionVBox.AddChild(_enemyActionTitleLabel);
        _enemyActionDescLabel = MakeLabel("", 11, false);
        _enemyActionDescLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        enemyActionVBox.AddChild(_enemyActionDescLabel);
        enemyActionHeader.AddChild(enemyActionVBox);
        _enemyActionPanel.AddChild(enemyActionHeader);

        var respPromptLabel = MakeLabel("選擇反應：", 11, true);
        _enemyActionPanel.AddChild(respPromptLabel);

        _responseListBox = new VBoxContainer();
        _responseListBox.AddThemeConstantOverride("separation", 3);
        _responseAcceptButton = MakeListEntry(_responseListBox, "承受", "0 AP", "受到全額傷害", "選擇", () => OnResponsePressed(new AcceptResponse()));
        _responseDodgeButton = MakeListEntry(_responseListBox, "迴避", "1 AP", "2d6+Skill vs 敵方命中，成功完全閃避", "選擇", () => OnResponsePressed(new DodgeResponse()));
        _responseBlockButton = MakeListEntry(_responseListBox, "格擋", "1 AP", "2d6+Power+盾值 vs 敵方攻擊，成功半減", "選擇", () => OnResponsePressed(new BlockResponse()));
        _responseCounterButton = MakeListEntry(_responseListBox, "反擊", "1 AP", "2d6+Power vs 敵方攻擊，成功反傷敵方", "選擇", () => OnResponsePressed(new CounterResponse()));
        _responseReflectButton = MakeListEntry(_responseListBox, "反射", "需反射卡 + 1 AP", "Phase 2 暫無反射卡資料（disabled）", "選擇", () => { /* no-op */ });
        _enemyActionPanel.AddChild(_responseListBox);
        leftBox.AddChild(_enemyActionPanel);

        // 結束戰鬥按鈕（Phase=Victory/Defeat/EnemyFled 顯示）
        _closeBattleButton = new Button { Text = "結束戰鬥", Visible = false };
        _closeBattleButton.AddThemeFontSizeOverride("font_size", 12);
        UiTheme.ApplyPrimaryButtonStyle(_closeBattleButton);
        _closeBattleButton.Pressed += () => { Hide(); EmitSignal(SignalName.BattleClosed); };
        leftBox.AddChild(_closeBattleButton);

        // 結算 label（次要顯示，主要靠 BattleLog）
        _resultLabel = MakeLabel("", 12, true);
        _resultLabel.AddThemeColorOverride("font_color", Palette.Gold);
        leftBox.AddChild(_resultLabel);

        // 右側：戰鬥日誌（最近高亮 + 多行 RichTextLabel）
        var rightBox = MakeSection("戰鬥日誌");
        rightBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rightBox.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        rightBox.SizeFlagsStretchRatio = 1.0f;
        _logCurrentLabel = MakeLabel("（尚無記錄）", 11, true);
        _logCurrentLabel.AutowrapMode = TextServer.AutowrapMode.Word;
        var currentBox = new PanelContainer();
        var currentStyle = new StyleBoxFlat
        {
            BgColor = Palette.PaperLight,
            BorderColor = Palette.OrnamentInk,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 4, ContentMarginBottom = 4,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        currentStyle.BorderWidthLeft = currentStyle.BorderWidthRight = currentStyle.BorderWidthTop = currentStyle.BorderWidthBottom = 1;
        currentBox.AddThemeStyleboxOverride("panel", currentStyle);
        currentBox.AddChild(_logCurrentLabel);
        rightBox.AddChild(currentBox);
        _battleLogText = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollFollowing = true,
            CustomMinimumSize = new Vector2(280, 200),
            FitContent = false,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        _battleLogText.AddThemeFontSizeOverride("normal_font_size", 11);
        _battleLogText.AddThemeColorOverride("default_color", Palette.InkLight);
        rightBox.AddChild(_battleLogText);
        mainRow.AddChild(rightBox);
    }

    // ─── Helpers ───────────────────────────────────────────────

    private static ProgressBar MakeHpBar(Color fillColor)
    {
        var bar = new ProgressBar
        {
            CustomMinimumSize = new Vector2(0, 12),
            ShowPercentage = false,
            MaxValue = 1, Value = 0,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = Palette.PaperDark });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fillColor });
        return bar;
    }

    private static VBoxContainer MakeSection(string title)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);
        var titleLabel = new Label { Text = title };
        titleLabel.AddThemeFontSizeOverride("font_size", 11);
        titleLabel.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        box.AddChild(titleLabel);
        return box;
    }

    /// <summary>列表項 row：左側 VBox（名稱 + cost + formula），右側 Button。共用於反應選項與基本行動列表。</summary>
    private Button MakeListEntry(VBoxContainer parent, string name, string cost, string formula, string buttonText, System.Action onPress)
    {
        var entryPanel = new PanelContainer();
        var entryStyle = new StyleBoxFlat
        {
            BgColor = Palette.PaperLight,
            BorderColor = Palette.InkLight,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 4, ContentMarginBottom = 4,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        entryStyle.BorderWidthLeft = entryStyle.BorderWidthRight = entryStyle.BorderWidthTop = entryStyle.BorderWidthBottom = 1;
        entryPanel.AddThemeStyleboxOverride("panel", entryStyle);
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);
        var infoVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        infoVBox.AddThemeConstantOverride("separation", 1);
        infoVBox.AddChild(MakeLabel(name, 12, true));
        var costLbl = MakeLabel(cost, 10, false);
        costLbl.AddThemeColorOverride("font_color", Palette.OrnamentInk);
        infoVBox.AddChild(costLbl);
        var formulaLbl = MakeLabel(formula, 10, false);
        formulaLbl.AutowrapMode = TextServer.AutowrapMode.Word;
        infoVBox.AddChild(formulaLbl);
        hbox.AddChild(infoVBox);
        var btn = new Button { Text = buttonText, Disabled = true, CustomMinimumSize = new Vector2(70, 0) };
        btn.AddThemeFontSizeOverride("font_size", 11);
        UiTheme.ApplyPrimaryButtonStyle(btn);
        btn.Pressed += () => onPress();
        hbox.AddChild(btn);
        entryPanel.AddChild(hbox);
        parent.AddChild(entryPanel);
        return btn;
    }

    private static Label MakeLabel(string text, int fontSize, bool bold)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", bold ? Palette.Ink : Palette.InkLight);
        return label;
    }

    private static string TierLabel(EncounterTier tier) => tier switch
    {
        EncounterTier.Advantage => "優勢（玩家先攻、完整揭露）",
        EncounterTier.Normal => "正常（玩家先攻、部分揭露）",
        EncounterTier.Ambushed => "劣勢（敵方先攻、情報全隱）",
        _ => "—",
    };

    private static string TierShort(EncounterTier tier) => tier switch
    {
        EncounterTier.Advantage => "優勢",
        EncounterTier.Normal => "正常",
        EncounterTier.Ambushed => "劣勢（被襲）",
        _ => "—",
    };

    private static string PhaseLabel(BattlePhase phase) => phase switch
    {
        BattlePhase.Start => "開始",
        BattlePhase.Encounter => "遭遇判定 (Phase A)",
        BattlePhase.PlayerTurn => "玩家回合 (Phase B)",
        BattlePhase.EnemyTurn => "敵方回合 (Phase C)",
        BattlePhase.AwaitingResponse => "等待玩家反應",
        BattlePhase.Victory => "勝利",
        BattlePhase.Defeat => "失敗",
        BattlePhase.EnemyFled => "敵方逃離",
        _ => phase.ToString(),
    };
}
