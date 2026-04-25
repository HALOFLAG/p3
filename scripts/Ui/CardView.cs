using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 可重用卡牌節點（規格書 §6.2 Compact 68×95；本版用 70×104 略大方便閱讀）。
/// 內含 type band（彩色頂條）+ name（中文卡名）+ cost（右上角數字）。
/// 點擊發 CardClicked signal 帶 CardId。
/// </summary>
public partial class CardView : PanelContainer
{
    [Signal] public delegate void CardClickedEventHandler(string cardId);

    public string CardId { get; private set; } = "";
    public string CardName { get; private set; } = "";
    public string CardType { get; private set; } = "";
    public int Cost { get; private set; }

    private Label? _nameLabel;
    private Label? _costLabel;
    private PanelContainer? _typeBand;
    private Label? _typeLabel;

    public override void _Ready()
    {
        // idempotent — Duplicate() 後再進 tree 不重建子元件，避免雙重 layout
        if (_nameLabel != null) return;

        CustomMinimumSize = new Vector2(70, 104);
        AddThemeStyleboxOverride("panel", BuildCardStyle());
        // 自身收 click + drag — 不能再有 child Button 攔截事件
        MouseFilter = MouseFilterEnum.Stop;

        var vbox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        vbox.AddThemeConstantOverride("separation", 2);
        AddChild(vbox);

        // 頂部 type band
        _typeBand = new PanelContainer { CustomMinimumSize = new Vector2(0, 16), MouseFilter = MouseFilterEnum.Ignore };
        _typeBand.AddThemeStyleboxOverride("panel", BuildTypeBandStyle(Palette.Purple));
        vbox.AddChild(_typeBand);
        _typeLabel = MakeLabel("?", 9, Palette.PaperLight);
        _typeLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _typeBand.AddChild(_typeLabel);

        // Cost 角標
        var topRow = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        topRow.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _costLabel = MakeLabel("0", 12, Palette.Ink);
        topRow.AddChild(_costLabel);
        vbox.AddChild(topRow);

        // 卡名（中央）
        _nameLabel = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _nameLabel.AddThemeFontSizeOverride("font_size", 11);
        _nameLabel.AddThemeColorOverride("font_color", Palette.Ink);
        _nameLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
        vbox.AddChild(_nameLabel);
    }

    /// <summary>
    /// click 偵測：mouse 釋放時觸發（按住但有拖曳會被 Godot 自動消耗，不會走到這裡）。
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
        {
            EmitSignal(SignalName.CardClicked, CardId);
            AcceptEvent();
        }
    }

    public void SetCard(string id, string name, string type, int cost)
    {
        CardId = id;
        CardName = name;
        CardType = type;
        Cost = cost;
        if (_nameLabel != null) _nameLabel.Text = name;
        if (_costLabel != null) _costLabel.Text = cost.ToString();
        if (_typeLabel != null) _typeLabel.Text = TypeDisplayName(type);
        if (_typeBand != null)
        {
            _typeBand.AddThemeStyleboxOverride("panel", BuildTypeBandStyle(TypeColor(type)));
        }
    }

    /// <summary>
    /// 規格書 §4.2.2 拖曳出牌：開始拖時建 preview 跟隨滑鼠，回傳 cardId 給 PlayCardZone 接收。
    /// </summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (string.IsNullOrEmpty(CardId)) return default;

        // Preview：簡易 banner（避免 Duplicate CardView 引發 _Ready 重跑問題）
        var preview = BuildDragPreview();
        SetDragPreview(preview);

        var data = new Godot.Collections.Dictionary
        {
            { "kind", "action_card" },
            { "card_id", CardId },
            { "card_name", CardName },
            { "card_type", CardType },
            { "card_cost", Cost },
        };
        return data;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragBegin)
        {
            Modulate = new Color(1, 1, 1, 0.35f); // 來源卡淡化
        }
        else if (what == NotificationDragEnd)
        {
            Modulate = Colors.White;
        }
    }

    /// <summary>
    /// 拖曳預覽 = 整張卡的複製品（用 Duplicate 取得結構與內容）。
    /// _Ready 已 idempotent，Duplicate 後再進入 tree 不會重建子元件。
    /// </summary>
    private Control BuildDragPreview()
    {
        var preview = (CardView)Duplicate();
        preview.MouseFilter = MouseFilterEnum.Ignore;
        preview.Modulate = new Color(1, 1, 1, 0.92f);
        // 包一層 Control 做置中偏移，讓滑鼠在卡片中央
        var wrapper = new Control { MouseFilter = MouseFilterEnum.Ignore };
        wrapper.AddChild(preview);
        preview.Position = new Vector2(-Size.X * 0.5f, -Size.Y * 0.5f);
        return wrapper;
    }

    private static string TypeDisplayName(string type) => type.ToLowerInvariant() switch
    {
        "combat" => "戰鬥",
        "exploration" => "探索",
        "communication" => "社交",
        "thinking" => "思考",
        _ => type,
    };

    private static Color TypeColor(string type) => type.ToLowerInvariant() switch
    {
        "combat" => Palette.Red,
        "exploration" => Palette.Green,
        "communication" => Palette.Blue,
        "thinking" => Palette.Purple,
        _ => Palette.OrnamentInk,
    };

    private static StyleBoxFlat BuildCardStyle() => new()
    {
        BgColor = Palette.Paper,
        BorderColor = Palette.Gold,
        BorderWidthLeft = 2, BorderWidthRight = 2,
        BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        ShadowColor = Palette.WithAlpha(Palette.Ink, 0.3f),
        ShadowSize = 4,
        ShadowOffset = new Vector2(2, 2),
        ContentMarginLeft = 4, ContentMarginRight = 4,
        ContentMarginTop = 0, ContentMarginBottom = 4,
    };

    private static StyleBoxFlat BuildTypeBandStyle(Color color) => new()
    {
        BgColor = color,
        ContentMarginLeft = 2, ContentMarginRight = 2,
        ContentMarginTop = 1, ContentMarginBottom = 1,
    };

    private static Label MakeLabel(string text, int size, Color color)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
