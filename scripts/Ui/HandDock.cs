using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 區塊 #16 — HandDock 手牌區（中欄底部，~864×120）。
/// Stage 1：實際手牌（從 WorldMap 訂閱 HandCardsChanged）+ 點擊出牌。
/// </summary>
public partial class HandDock : PanelContainer
{
    [Signal] public delegate void CardClickedExtEventHandler(string cardId);

    private HBoxContainer? _cardRow;
    private Label? _deckCountLabel;
    private Label? _discardCountLabel;
    private PackedScene? _cardViewScene;

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Palette.PaperDark,
            BorderColor = Palette.Ink,
            BorderWidthTop = 1,
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        });

        _cardViewScene = ResourceLoader.Load<PackedScene>("res://scenes/ui/card_view.tscn");

        var hbox = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        hbox.AddThemeConstantOverride("separation", 8);
        AddChild(hbox);

        // 預留左側 ~220 px 寬度，讓背包展開時 (slots 1, 2 + frame 約 210 px) 不會覆蓋到手牌
        var leftSpacer = new Control
        {
            CustomMinimumSize = new Vector2(220, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        hbox.AddChild(leftSpacer);

        _cardRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
        _cardRow.AddThemeConstantOverride("separation", 6);
        hbox.AddChild(_cardRow);

        // 預設 placeholder（無手牌時的提示）
        var placeholder = MakeColoredLabel("（尚未載入手牌）", 11, Palette.OrnamentInk);
        placeholder.Name = "Placeholder";
        _cardRow.AddChild(placeholder);

        // spacer
        hbox.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        // 牌堆指示
        var deckCol = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        hbox.AddChild(deckCol);
        deckCol.AddChild(MakeColoredLabel("抽牌堆", 10, Palette.OrnamentInk));
        _deckCountLabel = MakeColoredLabel("0", 18, Palette.Gold);
        _deckCountLabel.HorizontalAlignment = HorizontalAlignment.Center;
        deckCol.AddChild(_deckCountLabel);

        var discardCol = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        hbox.AddChild(discardCol);
        discardCol.AddChild(MakeColoredLabel("棄牌堆", 10, Palette.OrnamentInk));
        _discardCountLabel = MakeColoredLabel("0", 18, Palette.OrnamentInk);
        _discardCountLabel.HorizontalAlignment = HorizontalAlignment.Center;
        discardCol.AddChild(_discardCountLabel);
    }

    public void OnHandCardsChanged(string[] cardIds, string[] cardNames, string[] cardTypes, int[] cardCosts)
    {
        if (_cardRow is null || _cardViewScene is null) return;
        // 清掉舊
        foreach (var child in _cardRow.GetChildren()) child.QueueFree();

        if (cardIds.Length == 0)
        {
            _cardRow.AddChild(MakeColoredLabel("（手牌空）", 11, Palette.OrnamentInk));
            return;
        }

        for (int i = 0; i < cardIds.Length; i++)
        {
            var card = _cardViewScene.Instantiate<CardView>();
            _cardRow.AddChild(card);
            card.SetCard(cardIds[i], cardNames[i], cardTypes[i], cardCosts[i]);
            string cid = cardIds[i];
            card.CardClicked += _ => EmitSignal(SignalName.CardClickedExt, cid);
        }
    }

    /// <summary>由 MainBootstrap 呼叫更新抽/棄牌堆計數。</summary>
    public void OnDeckCountsChanged(int drawCount, int discardCount)
    {
        if (_deckCountLabel != null) _deckCountLabel.Text = drawCount.ToString();
        if (_discardCountLabel != null) _discardCountLabel.Text = discardCount.ToString();
    }

    private static Label MakeColoredLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
