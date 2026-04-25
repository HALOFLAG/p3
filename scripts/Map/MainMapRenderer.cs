using System;
using System.Linq;
using CardNarrative.Core.Map;
using CardNarrative.Core.Services;
using Godot;
using Projection = CardNarrative.Core.Map.Projection;
using ProjectionParams = CardNarrative.Core.Map.ProjectionParams;
using HauntedManor.Scripts.Ui;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Map;

/// <summary>
/// Phase 1 Task 2/4/5/6 — 9×9 主地圖渲染器（規格書 §2.1 / §5.1）。
/// UI v2 重構：HUD 拆出 → 對外發 Signal 給 TopBar / RightPanel 訂閱。
/// 自身只負責：投影渲染、popup、確認對話框、tile 點擊。
/// </summary>
public partial class MainMapRenderer : Control
{
    [Export] public PackedScene? TileVisualScene { get; set; }

    private readonly WorldMap _worldMap = new();
    private readonly TileVisual[,] _tileNodes = new TileVisual[WorldMap.Size, WorldMap.Size];
    private Node2D? _tileLayer;
    private ProjectionParams _projection;

    // Popup / dialog
    private ActionTriggerPopup? _popup;
    private ConfirmationDialog? _moveConfirm;
    private (int Row, int Col)? _pendingMoveTarget;

    // Demo Skill 屬性（規格書 §3.3 觀察用 = 綠探索）
    private const int DemoSkill = 3;
    private const int ObserveTn = 10;
    private readonly IDiceService _dice = new SeededDiceService(seed: Random.Shared.Next());

    public WorldMap WorldMap => _worldMap;

    // === 對外 Signals（取代內嵌 HUD）===

    /// <summary>牌堆狀態變更：發送 (剩餘張數, 持有, NEXT[0], NEXT[1])。空字串代表 -。</summary>
    [Signal] public delegate void DeckStatusChangedEventHandler(int remaining, string heldTerrain, string previewTop, string previewSecond);

    /// <summary>互動模式變更（中文標籤）。</summary>
    [Signal] public delegate void ModeChangedExtEventHandler(string modeLabel);

    /// <summary>HP 變更。</summary>
    [Signal] public delegate void HpChangedExtEventHandler(int hp, int hpMax);

    /// <summary>玩家移動完成（用於 TopBar 顯示位置 / RightPanel 收 log）。</summary>
    [Signal] public delegate void PlayerPositionChangedEventHandler(int row, int col);

    /// <summary>TURN LOG 新增一行。</summary>
    [Signal] public delegate void LogAppendedEventHandler(string line);

    public override void _Ready()
    {
        _tileLayer = GetNode<Node2D>("TileLayer");
        var w = (float)Size.X;
        var h = (float)Size.Y;
        _projection = Projection.Default with
        {
            ViewWidth = w,
            ViewHeight = h,
            // BaseTileSize 自適應 viewport：5×5 視野下 5 cols / 5 rows 都能容下
            BaseTileSize = Mathf.Min(w, h) / 6f,
            // VanishingPoint / GroundY 改為相對視框比例
            VanishingPointY = h * 0.32f,
            GroundY = h,
        };

        TileVisualScene ??= ResourceLoader.Load<PackedScene>("res://scenes/map/tile_visual.tscn");

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
            // ConfirmationDialog 是獨立 Window，不繼承父 Control 的 Theme → 顯式套用
            _moveConfirm.Theme = UiTheme.Build();
            // 移除 Godot 預設標題列（標題會塞進 dialog_text 開頭，整段都在金邊框內）
            _moveConfirm.Borderless = true;
            _moveConfirm.Title = "";
            // OK 按鈕（確認移動）→ Primary 紅底白字
            UiTheme.ApplyPrimaryButtonStyle(_moveConfirm.GetOkButton());
            _moveConfirm.Confirmed += OnMoveConfirmed;
            _moveConfirm.Canceled += OnMoveCanceled;
        }

        SpawnAllTiles();
        SubscribeWorldMap();

        var resetBtn = GetNodeOrNull<Button>("ResetButtonContainer/ResetButton");
        if (resetBtn != null) resetBtn.Pressed += OnResetPressed;

        // 視框尺寸可能還是 0（若被父 container 排版尚未完成）→ 等下一個 frame 重算
        CallDeferred(nameof(InitialLayout));
    }

    private void InitialLayout()
    {
        var w = (float)Size.X;
        var h = (float)Size.Y;
        if (w > 0 && h > 0)
        {
            _projection = _projection with
            {
                ViewWidth = w,
                ViewHeight = h,
                BaseTileSize = Mathf.Min(w, h) / 6f,
                VanishingPointY = h * 0.32f,
                GroundY = h,
            };
        }
        UpdateAllTiles();
        EmitHudSignals();
        AppendLog($"歡迎來到廢棄洋房。玩家位於 ({_worldMap.PlayerPos.Row},{_worldMap.PlayerPos.Col})。");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            var w = (float)Size.X;
            var h = (float)Size.Y;
            if (w > 0 && h > 0)
            {
                _projection = _projection with
                {
                    ViewWidth = w,
                    ViewHeight = h,
                    BaseTileSize = Mathf.Min(w, h) / 6f,
                    VanishingPointY = h * 0.32f,
                    GroundY = h,
                };
                if (_tileLayer != null && _tileNodes[0, 0] != null) UpdateAllTiles();
            }
        }
    }

    public override void _ExitTree()
    {
        UnsubscribeWorldMap();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } mb) return;

        if (mb.ButtonIndex == MouseButton.Left)
        {
            if (_popup is { Visible: true } popup)
            {
                var popupRect = new Rect2(popup.Position, popup.Size);
                if (!popupRect.HasPoint(mb.Position))
                {
                    popup.HidePopup();
                    AcceptEvent();
                    return;
                }
                return;
            }

            var tile = FindClickedTile(mb.Position);
            if (tile.HasValue)
            {
                OnTileClicked(tile.Value.row, tile.Value.col);
                AcceptEvent();
            }
        }
        else if (mb.ButtonIndex == MouseButton.Right)
        {
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

    // === 公開 API（給 TopBar 等外部呼叫）===

    /// <summary>外部觸發抽下一張地塊（取代原本內嵌 HUD 按鈕）。</summary>
    public void RequestDrawTile()
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
            // TileClicked signal 仍保留作為診斷/備用，主 click 走 _GuiInput
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
        EmitSignal(SignalName.PlayerPositionChanged, newRow, newCol);
        UpdateAllTiles();
    }

    private void OnModeChanged()
    {
        EmitHudSignals();
        UpdateAllTiles();
    }

    private void OnTilePlaced(MapTerrain terrain, int row, int col)
    {
        AppendLog($"放置地塊：{terrain} 於 ({row},{col})");
    }

    private void OnHpChanged(int hp)
    {
        EmitSignal(SignalName.HpChangedExt, hp, _worldMap.HpMax);
    }

    // === Tile click dispatch ===

    private void OnTileClicked(int row, int col)
    {
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
                        // 標題塞進 dialog_text 開頭，避免 Godot 內建標題列跑到金邊框外
                        _moveConfirm.DialogText = $"【 確認移動 】\n\n移動到地塊 ({row}, {col}) ？";
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
        _popup.ShowAt(node.Position);
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

    private void OnTalkSelected() => AppendLog("對話：這個地塊沒有可對話對象。");

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
            if (node is null) continue;
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

            TileVisual.OverlayKind overlay = TileVisual.OverlayKind.None;
            if (r == playerRow && c == playerCol)
                overlay = TileVisual.OverlayKind.PlayerMark;
            else if (_worldMap.Mode == InteractionMode.MapExpand && _worldMap.IsLegalPlacement(r, c))
                overlay = TileVisual.OverlayKind.LegalPlacement;
            else if (_worldMap.Mode == InteractionMode.Move && _worldMap.IsLegalMoveTarget(r, c))
                overlay = TileVisual.OverlayKind.MoveTarget;
            node.SetOverlay(overlay);
        }
    }

    private void EmitHudSignals()
    {
        var preview = _worldMap.NextTilePreview;
        var top = preview.Count > 0 ? preview[0].ToString() : "";
        var second = preview.Count > 1 ? preview[1].ToString() : "";
        EmitSignal(
            SignalName.DeckStatusChanged,
            _worldMap.RemainingTiles,
            _worldMap.HeldTile?.ToString() ?? "",
            top, second);

        var modeLabel = _worldMap.Mode switch
        {
            InteractionMode.Idle => "待命",
            InteractionMode.MapExpand => "放置地塊",
            InteractionMode.Move => "選擇移動目標",
            _ => "?",
        };
        EmitSignal(SignalName.ModeChangedExt, modeLabel);
        EmitSignal(SignalName.HpChangedExt, _worldMap.Hp, _worldMap.HpMax);
    }

    private void AppendLog(string line)
    {
        EmitSignal(SignalName.LogAppended, line);
        EmitHudSignals();
    }
}
