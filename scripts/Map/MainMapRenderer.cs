using System;
using System.Linq;
using CardNarrative.Core.Map;
using CardNarrative.Core.Services;
using Godot;
using Projection = CardNarrative.Core.Map.Projection;
using ProjectionParams = CardNarrative.Core.Map.ProjectionParams;
using HauntedManor.Scripts.Ui;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// Phase 1 Task 2/4/5/6 — 9×9 主地圖渲染器（規格書 §2.1 / §5.1）。
/// 持有 WorldMap、生成 81 個 TileVisual、處理：
///   - 投影渲染（已放置/未放置/合法區/移動目標 overlay）
///   - 玩家行動觸發器 popup（點玩家格 → 4 行動）
///   - MapExpand 模式（持地塊放合法區）
///   - 移動模式（選相鄰格 → 確認對話框）
///   - 觀察判定（2d6 + Skill vs TN 10）
///   - HUD 文字更新與 TURN LOG
/// </summary>
public partial class MainMapRenderer : Control
{
    [Export] public PackedScene? TileVisualScene { get; set; }

    private readonly WorldMap _worldMap = new();
    private readonly TileVisual[,] _tileNodes = new TileVisual[WorldMap.Size, WorldMap.Size];
    private Node2D? _tileLayer;
    private ProjectionParams _projection;

    // HUD
    private Label? _deckLabel;
    private Label? _heldLabel;
    private Label? _nextPreviewLabel;
    private Label? _modeLabel;
    private Label? _hpLabel;
    private Label? _turnLog;

    // Popup / dialog
    private ActionTriggerPopup? _popup;
    private ConfirmationDialog? _moveConfirm;

    // Movement intent (set when player clicks a target in Move mode)
    private (int Row, int Col)? _pendingMoveTarget;

    // Demo Skill 屬性（規格書 §3.3 觀察用 = 綠探索；本 demo 暫定 = 3）
    private const int DemoSkill = 3;
    private const int ObserveTn = 10;
    private readonly IDiceService _dice = new SeededDiceService(seed: Random.Shared.Next());

    public WorldMap WorldMap => _worldMap;

    public override void _Ready()
    {
        _tileLayer = GetNode<Node2D>("TileLayer");
        _projection = Projection.Default with
        {
            ViewWidth = (float)Size.X,
            ViewHeight = (float)Size.Y,
        };

        TileVisualScene ??= ResourceLoader.Load<PackedScene>("res://scenes/map/tile_visual.tscn");

        // HUD nodes
        _deckLabel = GetNodeOrNull<Label>("Hud/DeckLabel");
        _heldLabel = GetNodeOrNull<Label>("Hud/HeldLabel");
        _nextPreviewLabel = GetNodeOrNull<Label>("Hud/NextPreviewLabel");
        _modeLabel = GetNodeOrNull<Label>("Hud/ModeLabel");
        _hpLabel = GetNodeOrNull<Label>("Hud/HpLabel");
        _turnLog = GetNodeOrNull<Label>("TurnLog");

        // Popup + dialog
        _popup = GetNodeOrNull<ActionTriggerPopup>("ActionTriggerPopup");
        _moveConfirm = GetNodeOrNull<ConfirmationDialog>("MoveConfirmDialog");

        if (_popup != null)
        {
            _popup.MoveSelected += OnMoveSelected;
            _popup.RestSelected += OnRestSelected;
            _popup.ObserveSelected += OnObserveSelected;
            _popup.TalkSelected += OnTalkSelected;
        }
        if (_moveConfirm != null)
        {
            _moveConfirm.Confirmed += OnMoveConfirmed;
            _moveConfirm.Canceled += OnMoveCanceled;
        }

        SpawnAllTiles();
        SubscribeWorldMap();

        var drawBtn = GetNodeOrNull<Button>("Hud/DrawTileButton");
        if (drawBtn != null) drawBtn.Pressed += OnDrawTilePressed;

        var resetBtn = GetNodeOrNull<Button>("Hud/ResetButton");
        if (resetBtn != null) resetBtn.Pressed += OnResetPressed;

        UpdateAllTiles();
        UpdateHud();
        AppendLog($"歡迎來到廢棄洋房。玩家位於 ({_worldMap.PlayerPos.Row},{_worldMap.PlayerPos.Col})。");
    }

    public override void _ExitTree()
    {
        UnsubscribeWorldMap();
    }

    /// <summary>
    /// Bug fix v2：改用 _GuiInput 直接接收 Control 區域內的點擊。
    /// Area2D + collision shape 在 Control 階層下不可靠（hover 走的路徑與 click 不同），
    /// 直接從螢幕座標反推到哪格更穩固。
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            // 1. popup 可見 + 點擊位置在 popup 外 → 關 popup
            if (_popup is { Visible: true } popup)
            {
                var popupRect = new Rect2(popup.Position, popup.Size);
                if (!popupRect.HasPoint(mb.Position))
                {
                    popup.HidePopup();
                    AcceptEvent();
                    return;
                }
                // popup 內部點擊 → 讓 button 自己處理
                return;
            }

            // 2. 反推點到哪格
            var tile = FindClickedTile(mb.Position);
            GD.Print($"[MainMapRenderer] _GuiInput click at {mb.Position} → tile {tile}");
            if (tile.HasValue)
            {
                OnTileClicked(tile.Value.row, tile.Value.col);
                AcceptEvent();
            }
        }
        else if (mb.ButtonIndex == MouseButton.Right)
        {
            // 規格書 §3.1.4 / §5.1.4：右鍵取消
            if (_worldMap.Mode == InteractionMode.Move)
            {
                _worldMap.CancelMoveMode();
                AppendLog("已取消移動。");
                AcceptEvent();
            }
            else if (_worldMap.Mode == InteractionMode.MapExpand)
            {
                _worldMap.CancelMapExpand();
                AppendLog("已取消放置（地塊放回牌堆）。");
                AcceptEvent();
            }
        }
    }

    /// <summary>
    /// 反推：給定 MainMap Control 內的局部座標，找出該位置覆蓋的可見地塊。
    /// 多格重疊時取中心距離最近者（單點透視下，多個格的 bounding box 可能重疊）。
    /// </summary>
    private (int row, int col)? FindClickedTile(Vector2 localPos)
    {
        var (playerRow, playerCol) = _worldMap.PlayerPos;
        var (offsetRow, offsetCol) = _worldMap.CameraOffset;

        float bestDistSq = float.MaxValue;
        (int row, int col)? best = null;

        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var relRow = (r - playerRow) - (int)Mathf.Round(offsetRow);
            var relCol = (c - playerCol) - (int)Mathf.Round(offsetCol);
            if (!Projection.IsVisible(relRow, relCol, _projection)) continue;

            var p = Projection.Project(relRow, relCol, _projection);
            var centerX = p.X + p.Width * 0.5f;
            var centerY = p.Y + p.Height * 0.5f;
            var halfSize = p.Width * 0.5f;

            if (Mathf.Abs(localPos.X - centerX) <= halfSize
                && Mathf.Abs(localPos.Y - centerY) <= halfSize)
            {
                var dx = localPos.X - centerX;
                var dy = localPos.Y - centerY;
                var dSq = dx * dx + dy * dy;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    best = (r, c);
                }
            }
        }
        return best;
    }

    // === Spawn / subscribe ===

    private void SpawnAllTiles()
    {
        if (_tileLayer is null || TileVisualScene is null)
        {
            GD.PrintErr("[MainMapRenderer] TileLayer 或 TileVisualScene 未設定");
            return;
        }
        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var node = TileVisualScene.Instantiate<TileVisual>();
            node.Name = $"Tile_{r}_{c}";
            node.Row = r;
            node.Col = c;
            node.TileClicked += OnTileClicked;
            _tileLayer.AddChild(node);
            _tileNodes[r, c] = node;
        }
    }

    private void SubscribeWorldMap()
    {
        _worldMap.TileChanged += OnTileChanged;
        _worldMap.PlayerMoved += OnPlayerMoved;
        _worldMap.CameraOffsetChanged += UpdateAllTiles;
        _worldMap.ModeChanged += OnModeChanged;
        _worldMap.TilePlaced += OnTilePlaced;
        _worldMap.HpChanged += OnHpChanged;
    }

    private void UnsubscribeWorldMap()
    {
        _worldMap.TileChanged -= OnTileChanged;
        _worldMap.PlayerMoved -= OnPlayerMoved;
        _worldMap.CameraOffsetChanged -= UpdateAllTiles;
        _worldMap.ModeChanged -= OnModeChanged;
        _worldMap.TilePlaced -= OnTilePlaced;
        _worldMap.HpChanged -= OnHpChanged;
    }

    // === WorldMap event handlers ===

    private void OnTileChanged(int row, int col)
    {
        var node = _tileNodes[row, col];
        var data = _worldMap.GetTile(row, col);
        node.SetTile(data.Terrain, data.IsPlaced, data.IsExplored);
    }

    private void OnPlayerMoved(int oldRow, int oldCol, int newRow, int newCol)
    {
        AppendLog($"玩家移動：({oldRow},{oldCol}) → ({newRow},{newCol})");
        UpdateAllTiles();
    }

    private void OnModeChanged()
    {
        UpdateHud();
        UpdateAllTiles();
    }

    private void OnTilePlaced(MapTerrain terrain, int row, int col)
    {
        AppendLog($"放置地塊：{terrain} 於 ({row},{col})");
    }

    private void OnHpChanged(int hp)
    {
        UpdateHud();
    }

    // === Tile click handler — entry point for MapExpand / Move / Popup ===

    private void OnTileClicked(int row, int col)
    {
        GD.Print($"[MainMapRenderer] tile clicked ({row},{col}) mode={_worldMap.Mode} player={_worldMap.PlayerPos}");
        switch (_worldMap.Mode)
        {
            case InteractionMode.MapExpand:
                if (_worldMap.IsLegalPlacement(row, col))
                {
                    _worldMap.TryPlaceHeldTile(row, col);
                }
                else
                {
                    AppendLog($"({row},{col}) 不是合法放置區。");
                }
                break;

            case InteractionMode.Move:
                if (_worldMap.IsLegalMoveTarget(row, col))
                {
                    _pendingMoveTarget = (row, col);
                    if (_moveConfirm != null)
                    {
                        _moveConfirm.DialogText = $"確認移動到地塊 ({row},{col})？";
                        _moveConfirm.PopupCentered();
                    }
                    else
                    {
                        _worldMap.TryMovePlayerTo(row, col);
                    }
                }
                else
                {
                    AppendLog($"({row},{col}) 不可移動（需 4 方向相鄰已放格）。");
                }
                break;

            case InteractionMode.Idle:
                // 規格書 §3.1.4：點角色所在格 → 彈出觸發器
                if ((row, col) == _worldMap.PlayerPos)
                {
                    ShowPopupAtPlayer();
                }
                break;
        }
    }

    private void ShowPopupAtPlayer()
    {
        if (_popup is null) return;
        var (pr, pc) = _worldMap.PlayerPos;
        var node = _tileNodes[pr, pc];
        var screenPos = node.Position; // node.Position 已是 Control 內座標
        _popup.ShowAt(screenPos);
    }

    // === Popup callbacks ===

    private void OnMoveSelected()
    {
        AppendLog("選擇行動：移動 → 點選相鄰已放格。");
        _worldMap.BeginMoveMode();
    }

    private void OnRestSelected()
    {
        if (_worldMap.Hp >= _worldMap.HpMax)
        {
            AppendLog("HP 已滿，無法休息。");
            return;
        }
        _worldMap.Rest();
        AppendLog($"休息：HP +1 → {_worldMap.Hp}/{_worldMap.HpMax}");
    }

    private void OnObserveSelected()
    {
        var roll = _dice.Roll2d6();
        var total = roll.Total + DemoSkill;
        var success = total >= ObserveTn;
        var marker = roll.IsDouble6 ? " ★雙6" : roll.IsDouble1 ? " ☠雙1" : "";
        AppendLog(
            $"觀察：2d6({roll.Total})+Skill({DemoSkill}) = {total} vs TN={ObserveTn} → "
            + (success ? "成功" : "失敗") + marker);
    }

    private void OnTalkSelected()
    {
        AppendLog("對話：這個地塊沒有可對話對象。");
    }

    // === ConfirmationDialog ===

    private void OnMoveConfirmed()
    {
        if (_pendingMoveTarget is { } target)
        {
            _worldMap.TryMovePlayerTo(target.Row, target.Col);
        }
        _pendingMoveTarget = null;
    }

    private void OnMoveCanceled()
    {
        _pendingMoveTarget = null;
        _worldMap.CancelMoveMode();
        AppendLog("已取消移動。");
    }

    // === HUD button handlers ===

    private void OnDrawTilePressed()
    {
        if (_worldMap.Mode != InteractionMode.Idle)
        {
            AppendLog("目前不是待命狀態，無法抽地塊。");
            return;
        }
        if (_worldMap.RemainingTiles == 0)
        {
            AppendLog("牌堆已空。");
            return;
        }
        _worldMap.BeginMapExpand();
        AppendLog($"抽到地塊：{_worldMap.HeldTile}，請點擊綠色合法區放置。");
    }

    private void OnResetPressed() => _worldMap.ResetCameraToPlayer();

    // === Layout ===

    private void UpdateAllTiles()
    {
        var (playerRow, playerCol) = _worldMap.PlayerPos;
        var (offsetRow, offsetCol) = _worldMap.CameraOffset;

        for (int r = 0; r < WorldMap.Size; r++)
        for (int c = 0; c < WorldMap.Size; c++)
        {
            var node = _tileNodes[r, c];
            var data = _worldMap.GetTile(r, c);

            var relRow = (r - playerRow) - (int)Mathf.Round(offsetRow);
            var relCol = (c - playerCol) - (int)Mathf.Round(offsetCol);

            if (!Projection.IsVisible(relRow, relCol, _projection))
            {
                node.Visible = false;
                continue;
            }

            var projected = Projection.Project(relRow, relCol, _projection);
            node.Visible = true;
            node.Position = new Vector2(
                projected.X + projected.Width * 0.5f,
                projected.Y + projected.Height * 0.5f);
            node.SetTile(data.Terrain, data.IsPlaced, data.IsExplored);
            node.SetTileSize(projected.Width);

            // Overlay：玩家標 / MapExpand 合法區 / Move 目標
            TileVisual.OverlayKind overlay = TileVisual.OverlayKind.None;
            if (r == playerRow && c == playerCol)
            {
                overlay = TileVisual.OverlayKind.PlayerMark;
            }
            else if (_worldMap.Mode == InteractionMode.MapExpand && _worldMap.IsLegalPlacement(r, c))
            {
                overlay = TileVisual.OverlayKind.LegalPlacement;
            }
            else if (_worldMap.Mode == InteractionMode.Move && _worldMap.IsLegalMoveTarget(r, c))
            {
                overlay = TileVisual.OverlayKind.MoveTarget;
            }
            node.SetOverlay(overlay);
        }
    }

    private void UpdateHud()
    {
        if (_deckLabel != null) _deckLabel.Text = $"牌堆: {_worldMap.RemainingTiles}";
        if (_heldLabel != null) _heldLabel.Text = $"持有: {(_worldMap.HeldTile?.ToString() ?? "-")}";
        if (_nextPreviewLabel != null)
        {
            var preview = _worldMap.NextTilePreview;
            var text = preview.Count switch
            {
                0 => "NEXT: -, -",
                1 => $"NEXT: {preview[0]}, -",
                _ => $"NEXT: {preview[0]}, {preview[1]}",
            };
            _nextPreviewLabel.Text = text;
        }
        if (_modeLabel != null)
        {
            _modeLabel.Text = "模式: " + _worldMap.Mode switch
            {
                InteractionMode.Idle => "待命",
                InteractionMode.MapExpand => "放置地塊",
                InteractionMode.Move => "選擇移動目標",
                _ => "?",
            };
        }
        if (_hpLabel != null) _hpLabel.Text = $"HP: {_worldMap.Hp}/{_worldMap.HpMax}";
    }

    private void AppendLog(string line)
    {
        if (_turnLog is null) return;
        var existing = _turnLog.Text ?? "";
        var lines = existing.Split('\n');
        // 保留最後 6 行
        var tail = string.Join('\n', lines.TakeLast(6));
        _turnLog.Text = tail + "\n" + line;
        UpdateHud();
    }
}
