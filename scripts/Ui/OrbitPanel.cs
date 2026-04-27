using System.Collections.Generic;
using CardNarrative.Core.Events;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 任務 9 / §4.2 區塊 #15 — ORBIT 任務板 UI：7 槽 + 第 8 展開鈕。
///
/// 排序：A 最左 → B → C，與 EventOrbit.VisibleSlotsView() 一致。
/// 4 欄 × 2 列 GridContainer，槽位用 OrbitCardView (70×104 同 §6.2 canonical)。
///
/// 視覺：
///   - ClassA：金底 type band（亮）
///   - ClassB：中棕 type band（灰感）
///   - ClassC：深墨 type band（卡背感）
///   - IsEnding：金邊變厚 + 亮底 + ★ 取代 TN
///
/// 此 panel 為「內容控件」— 不帶自己的 header，由父容器（RightPanel）負責 header。
/// 由 MainBootstrap 透過 SetOrbit(EventOrbit) 注入，並直接訂閱 Core EventOrbit.OrbitChanged
/// （繞過 EventBus 因為子節點 _Ready 在父節點之前，EventBus.Instance 此時可能還未就緒）。
/// </summary>
public partial class OrbitPanel : GridContainer
{
    private const int SlotCount = 7;
    private const int ColumnsCount = 4;

    /// <summary>玩家點擊 ORBIT 卡時 emit；caller (MainBootstrap) 通常只在 ClassA 才打開結算對話框。</summary>
    [Signal]
    public delegate void EventCardClickedEventHandler(string eventId);

    private EventOrbit? _orbit;
    private readonly OrbitCardView[] _slots = new OrbitCardView[SlotCount];
    private Button? _expandButton;
    private AcceptDialog? _expandDialog;
    private GridContainer? _expandGrid;
    private Tween? _pulseTween;

    public override void _Ready()
    {
        Columns = ColumnsCount;
        AddThemeConstantOverride("h_separation", 4);
        AddThemeConstantOverride("v_separation", 4);

        for (int i = 0; i < SlotCount; i++)
        {
            var card = new OrbitCardView();
            AddChild(card);
            card.SetEmpty(); // 初始空槽，等 SetOrbit 後 Refresh 填入
            card.Clicked += () => OnSlotClicked(card);
            _slots[i] = card;
        }

        _expandButton = new Button
        {
            Text = "＋\n展開",
            TooltipText = "展開全部",
            CustomMinimumSize = new Vector2(OrbitCardView.CardWidth, OrbitCardView.CardHeight),
            Visible = false,
        };
        _expandButton.AddThemeFontSizeOverride("font_size", 14);
        _expandButton.AddThemeColorOverride("font_color", Palette.Ink);
        _expandButton.Pressed += OnExpandPressed;
        AddChild(_expandButton);

        // 注意：不在此訂閱 EventBus —
        // Godot 子節點 _Ready 在父節點之前，EventBus.Instance 此時可能還是 null。
        // 改在 SetOrbit() 直接訂閱 Core EventOrbit.OrbitChanged（保證已就緒）。
    }

    public override void _ExitTree()
    {
        if (_orbit is not null)
        {
            _orbit.OrbitChanged -= OnCoreOrbitChanged;
        }
    }

    /// <summary>由 MainBootstrap 注入；同步直接訂閱 Core OrbitChanged event 並做首次 Refresh。</summary>
    public void SetOrbit(EventOrbit orbit)
    {
        if (_orbit is not null)
            _orbit.OrbitChanged -= OnCoreOrbitChanged;
        _orbit = orbit;
        _orbit.OrbitChanged += OnCoreOrbitChanged;
        Refresh();
    }

    private void OnCoreOrbitChanged()
    {
        // Core 可能於背景執行緒呼叫；CallDeferred 確保 redraw 在主執行緒
        CallDeferred(MethodName.Refresh);
    }

    private void OnSlotClicked(OrbitCardView card)
    {
        if (card.CurrentInstance is null) return;
        EmitSignal(SignalName.EventCardClicked, card.CurrentInstance.Card.Id);
    }

    /// <summary>
    /// Task 12 Stage 4 — 訊息氣泡點擊跳轉觸發；整個 ORBIT 區金色脈衝 1 秒（200ms 淡入 → 600ms hold → 200ms 淡出）。
    /// 模式沿用 TileVisual.TriggerPulseHighlight。對 self.Modulate 做 tween — 繼承到所有 OrbitCardView child。
    /// </summary>
    public void TriggerPulseHighlight()
    {
        _pulseTween?.Kill();
        Modulate = Colors.White;
        var golden = new Color(1.5f, 1.3f, 0.7f);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate", golden, 0.2);
        tween.TweenInterval(0.6);
        tween.TweenProperty(this, "modulate", Colors.White, 0.2);
        _pulseTween = tween;
    }

    public void Refresh()
    {
        if (_orbit is null) return;

        var visible = _orbit.VisibleSlotsView();
        for (int i = 0; i < SlotCount; i++)
        {
            if (i < visible.Count)
                _slots[i].SetEvent(visible[i]);
            else
                _slots[i].SetEmpty();
        }

        if (_expandButton is not null)
            _expandButton.Visible = _orbit.HasExpandButton;
    }

    private void OnExpandPressed()
    {
        if (_orbit is null) return;

        // 4 欄 × N 列卡片 grid，最多顯示 6 列（24 張）— 超過時 ScrollContainer 處理
        const int dialogColumns = 4;
        const int sep = 4;
        int contentWidth = dialogColumns * OrbitCardView.CardWidth + (dialogColumns - 1) * sep;
        int maxVisibleRows = 6;
        int contentHeight = maxVisibleRows * OrbitCardView.CardHeight + (maxVisibleRows - 1) * sep;

        if (_expandDialog is null)
        {
            _expandDialog = new AcceptDialog
            {
                Title = "ORBIT 全部事件",
                MinSize = new Vector2I(contentWidth + 32, contentHeight + 80),
            };
            AddChild(_expandDialog);

            var scroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(contentWidth + 16, contentHeight),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            };
            _expandDialog.AddChild(scroll);

            _expandGrid = new GridContainer
            {
                Columns = dialogColumns,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            _expandGrid.AddThemeConstantOverride("h_separation", sep);
            _expandGrid.AddThemeConstantOverride("v_separation", sep);
            scroll.AddChild(_expandGrid);
        }

        // 清掉舊卡片，依當前 ORBIT 重建
        if (_expandGrid is not null)
        {
            foreach (var child in _expandGrid.GetChildren())
            {
                _expandGrid.RemoveChild(child);
                child.QueueFree();
            }
            foreach (var inst in _orbit.ExpandedView())
            {
                var card = new OrbitCardView();
                _expandGrid.AddChild(card);
                card.SetEvent(inst);
            }
        }

        _expandDialog.PopupCentered();
    }
}
