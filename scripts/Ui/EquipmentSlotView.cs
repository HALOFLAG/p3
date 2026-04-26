using System.Collections.Generic;
using System.Linq;
using CardNarrative.Core.Models;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 裝備格單一槽位（規格書 §3.4.3 + §4.2 區塊 #8）。
/// 兩種模式：Primary（主槽 enum）/ Backpack（背包 index）。
///
/// 用 plain Control + 顯式 absolute layout（不繼承 PanelContainer），
/// 確保在 Container 與 plain-Control parent 下高度都是 98×138、卡片都是 90×130。
///
/// 拖曳：
/// - _GetDragData：若有裝備 + IsDraggable，回 dictionary {kind:equipment, eq_id, source_kind, source_slot}
/// - _CanDropData / _DropData：依 Catalog 判斷相容性；不相容拒絕
/// - 拖曳開始時（NotificationDragBegin），全域檢查並對相容槽顯示白色半透明高亮
/// - drag preview 為整張 EquipmentCardView（與 CardView 體感一致）
/// </summary>
public partial class EquipmentSlotView : Control
{
    [Signal] public delegate void EquipmentDroppedEventHandler(string sourceKind, int sourceIdx, string targetKind, int targetIdx);

    public enum SlotKind { Primary, Backpack }

    /// <summary>外框尺寸（略大於內含的 EquipmentCardView 90×130，預留 4px padding）。</summary>
    public const int CardWidth = 98;
    public const int CardHeight = 138;
    private const int InnerPad = 4;

    public SlotKind Kind { get; private set; } = SlotKind.Primary;
    public EquipmentSlot PrimarySlot { get; private set; } = EquipmentSlot.Head;
    public int BackpackIndex { get; private set; }
    public string SlotLabel { get; private set; } = "";
    public string? EquippedId { get; private set; }
    public Equipment? EquippedItem { get; private set; }

    /// <summary>角色卡是否佔住此格（與 EquippedItem 互斥；占住時槽不顯示裝備）。</summary>
    public Character? CharacterCard { get; private set; }

    /// <summary>是否可被拖曳（由 BackpackBar 控制：摺疊時只有 top 可拖）。</summary>
    public bool IsDraggable { get; set; } = true;

    /// <summary>裝備目錄參考（LeftPanel 注入）。</summary>
    public IReadOnlyDictionary<string, Equipment>? Catalog { get; set; }

    private Panel? _bgPanel;
    private EquipmentCardView? _cardView;
    private CharacterCardView? _characterView;
    private Panel? _hoverOverlay;

    public override void _Ready()
    {
        if (_cardView != null) return; // idempotent
        CustomMinimumSize = new Vector2(CardWidth, CardHeight);
        Size = new Vector2(CardWidth, CardHeight); // 顯式，避免 plain-Control parent 留 0
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        MouseFilter = MouseFilterEnum.Stop;

        // 外框 = 一張底層 Panel，FullRect anchors，自動跟隨 slot 大小
        _bgPanel = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        _bgPanel.AddThemeStyleboxOverride("panel", BuildSlotStyle(HoverState.Idle));
        AddChild(_bgPanel);
        _bgPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // 卡片在 (4, 4)，顯式 Size 90×130
        _cardView = new EquipmentCardView();
        AddChild(_cardView);
        _cardView.Position = new Vector2(InnerPad, InnerPad);
        _cardView.Size = new Vector2(EquipmentCardView.CardWidth, EquipmentCardView.CardHeight);
        _cardView.CustomMinimumSize = new Vector2(EquipmentCardView.CardWidth, EquipmentCardView.CardHeight);

        _characterView = new CharacterCardView { Visible = false };
        AddChild(_characterView);
        _characterView.Position = new Vector2(InnerPad, InnerPad);
        _characterView.Size = new Vector2(CharacterCardView.CardWidth, CharacterCardView.CardHeight);
        _characterView.CustomMinimumSize = new Vector2(CharacterCardView.CardWidth, CharacterCardView.CardHeight);

        // 拖曳時的白色半透明覆蓋層（蓋住整張卡）
        _hoverOverlay = new Panel { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_hoverOverlay);
        _hoverOverlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        Refresh();
    }

    public void ConfigurePrimary(EquipmentSlot slot, string label)
    {
        Kind = SlotKind.Primary;
        PrimarySlot = slot;
        SlotLabel = label;
        Refresh();
    }

    public void ConfigureBackpack(int index)
    {
        Kind = SlotKind.Backpack;
        BackpackIndex = index;
        SlotLabel = $"背包 {index + 1}";
        Refresh();
    }

    public void SetEquipment(string? equipmentId, Equipment? item)
    {
        EquippedId = equipmentId;
        EquippedItem = item;
        // 角色卡與裝備互斥：呼叫此方法即視為「此槽現在不是角色卡」
        // 不能只在 item != null 才清，否則角色卡離開後舊槽會殘留 CharacterCard 引用，
        // 造成新舊兩格同時渲染同一張角色卡。
        CharacterCard = null;
        Refresh();
    }

    /// <summary>把角色卡放進此格（裝備自動清空，與 SetEquipment 互斥）。</summary>
    public void SetCharacterCard(Character? character)
    {
        CharacterCard = character;
        if (character is not null)
        {
            EquippedId = null;
            EquippedItem = null;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (CharacterCard is not null)
        {
            if (_cardView != null) _cardView.Visible = false;
            if (_characterView != null)
            {
                _characterView.Visible = true;
                _characterView.SetCharacter(CharacterCard);
            }
        }
        else
        {
            if (_characterView != null) _characterView.Visible = false;
            if (_cardView != null)
            {
                _cardView.Visible = true;
                _cardView.SetEquipment(EquippedItem, SlotLabel);
            }
        }
    }

    // === 拖曳 ===

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!IsDraggable) return default;
        if (CharacterCard is not null && Kind == SlotKind.Primary)
        {
            var charData = new Godot.Collections.Dictionary
            {
                { "kind", "character_card" },
                { "source_kind", "primary" },
                { "source_slot", (int)PrimarySlot },
            };
            SetDragPreview(BuildCharacterDragPreview());
            return charData;
        }
        if (string.IsNullOrEmpty(EquippedId)) return default;
        var data = new Godot.Collections.Dictionary
        {
            { "kind", "equipment" },
            { "eq_id", EquippedId },
            { "source_kind", Kind == SlotKind.Primary ? "primary" : "backpack" },
            { "source_slot", Kind == SlotKind.Primary ? (int)PrimarySlot : BackpackIndex },
        };
        SetDragPreview(BuildDragPreview());
        return data;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!CanAcceptDrag(data)) { SetHoverState(HoverState.Idle); return false; }
        SetHoverState(HoverState.Hover);
        return true;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!CanAcceptDrag(data)) return;
        var dict = data.As<Godot.Collections.Dictionary>();
        var kind = dict["kind"].AsString();
        var srcKind = kind == "character_card" ? "character" : dict["source_kind"].AsString();
        var srcIdx = dict["source_slot"].AsInt32();
        SetHoverState(HoverState.Idle);
        var targetKind = Kind == SlotKind.Primary ? "primary" : "backpack";
        var targetIdx = Kind == SlotKind.Primary ? (int)PrimarySlot : BackpackIndex;
        EmitSignal(SignalName.EquipmentDropped, srcKind, srcIdx, targetKind, targetIdx);
    }

    public override void _Notification(int what)
    {
        switch ((long)what)
        {
            case NotificationDragBegin:
                var drag = GetViewport().GuiGetDragData();
                if (CanAcceptDrag(drag)) SetHoverState(HoverState.Compatible);
                break;
            case NotificationDragEnd:
                SetHoverState(HoverState.Idle);
                break;
        }
    }

    private bool CanAcceptDrag(Variant data)
    {
        var dict = data.As<Godot.Collections.Dictionary>();
        if (dict is null || !dict.ContainsKey("kind")) return false;
        var kind = dict["kind"].AsString();
        if (kind == "character_card")
        {
            // 角色卡禁背包；主槽永遠接受（含目標已有裝備時的 swap）
            if (Kind == SlotKind.Backpack) return false;
            var srcSlot = dict["source_slot"].AsInt32();
            if ((int)PrimarySlot == srcSlot) return false;
            return true;
        }
        if (kind != "equipment") return false;

        var srcKind = dict["source_kind"].AsString();
        var srcIdx = dict["source_slot"].AsInt32();
        if (IsSameSlot(srcKind, srcIdx)) return false;

        // 目標槽住有角色卡：來源若是背包則拒絕（角色卡無處可去）；主槽 swap 由 LeftPanel 後續路徑處理。
        if (Kind == SlotKind.Primary && CharacterCard is not null && srcKind == "backpack") return false;

        if (Kind == SlotKind.Backpack) return true;
        if (Catalog is null) return true;
        var eqId = dict["eq_id"].AsString();
        if (!Catalog.TryGetValue(eqId, out var eq)) return true;
        return eq.EffectiveAllowedSlots.Contains(PrimarySlot);
    }

    private bool IsSameSlot(string srcKind, int srcIdx)
    {
        if (Kind == SlotKind.Primary)
            return srcKind == "primary" && srcIdx == (int)PrimarySlot;
        return srcKind == "backpack" && srcIdx == BackpackIndex;
    }

    private enum HoverState { Idle, Compatible, Hover }

    private void SetHoverState(HoverState state)
    {
        if (_hoverOverlay == null) return;
        switch (state)
        {
            case HoverState.Idle:
                _hoverOverlay.Visible = false;
                break;
            case HoverState.Compatible:
                _hoverOverlay.Visible = true;
                _hoverOverlay.AddThemeStyleboxOverride("panel", BuildOverlayStyle(0.30f, 0.7f));
                break;
            case HoverState.Hover:
                _hoverOverlay.Visible = true;
                _hoverOverlay.AddThemeStyleboxOverride("panel", BuildOverlayStyle(0.55f, 1.0f));
                break;
        }
    }

    private static StyleBoxFlat BuildSlotStyle(HoverState _) => new()
    {
        BgColor = Palette.Paper,
        BorderColor = Palette.InkLight,
        BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2,
        CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
    };

    private static StyleBoxFlat BuildOverlayStyle(float bgAlpha, float borderAlpha) => new()
    {
        BgColor = new Color(1, 1, 1, bgAlpha),
        BorderColor = new Color(1, 1, 1, borderAlpha),
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
    };

    private Control BuildDragPreview()
    {
        var card = new EquipmentCardView();
        card.MouseFilter = MouseFilterEnum.Ignore;

        var wrapper = MakeTopLevelDragWrapper();
        wrapper.AddChild(card);
        var w = EquipmentCardView.CardWidth;
        var h = EquipmentCardView.CardHeight;
        card.Position = new Vector2(-w * 0.5f, -h * 0.5f);
        card.Size = new Vector2(w, h);
        card.SetEquipment(EquippedItem);
        return wrapper;
    }

    private Control BuildCharacterDragPreview()
    {
        var card = new CharacterCardView();
        card.MouseFilter = MouseFilterEnum.Ignore;
        var wrapper = MakeTopLevelDragWrapper();
        wrapper.AddChild(card);
        var w = CharacterCardView.CardWidth;
        var h = CharacterCardView.CardHeight;
        card.Position = new Vector2(-w * 0.5f, -h * 0.5f);
        card.Size = new Vector2(w, h);
        card.SetCharacter(CharacterCard);
        return wrapper;
    }

    /// <summary>
    /// drag preview 必須蓋過 ParallaxScene (ZIndex=5) 與其它 Z 加層的元件；
    /// 用 Godot ZIndex 上限 + ZAsRelative=false 鎖到絕對最上層。
    /// </summary>
    private static Control MakeTopLevelDragWrapper() => new()
    {
        MouseFilter = MouseFilterEnum.Ignore,
        ZIndex = 4096,
        ZAsRelative = false,
    };
}
