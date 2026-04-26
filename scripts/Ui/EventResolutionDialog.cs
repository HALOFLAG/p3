using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using Godot;
using HauntedManor.Scripts.Theme;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// 任務 9 / §1.6 EventCheck — 事件結算對話框。
///
/// 對應規格 §1.3 + §1.6：A 類事件被觸發後玩家走「擲骰 → 結果分支 → 確認」流程。
/// UI 結構參考舊版 <c>oldplan_p2/p2/.../EventResolutionView.axaml</c> 的三段式 layout，
/// 但以 Godot Control 重寫；目前不接 GameState（玩家 Stat=0、無實際 Effects 套用），
/// 屬於 demo / L2 視覺驗證用，後續接 TurnLoop 後再補上 effect handler 路徑。
///
/// 公開：
///   - Open(EventInstance) — 顯示對話框並重置狀態
///   - EventResolved(string eventId, int tier) Signal — 玩家按「確認」後 emit
/// </summary>
public partial class EventResolutionDialog : AcceptDialog
{
    /// <summary>玩家確認結算後 emit；caller 應從 ORBIT 移除事件。tier: 0=Success, 1=PartialSuccess, 2=Failure。</summary>
    [Signal]
    public delegate void EventResolvedEventHandler(string eventId, int tier);

    private EventInstance? _instance;
    private bool _hasRolled;
    private EventOutcomeTier _tier;
    private readonly IDiceService _dice = new SeededDiceService(System.Environment.TickCount);

    private Label? _statTnLabel;
    private Label? _narrativeLabel;
    private Label? _diceLabel;
    private Label? _outcomeTierLabel;
    private Label? _outcomeNarrativeLabel;
    private Button? _rollButton;
    private Button? _confirmButton;
    private PanelContainer? _companionHintBox;
    private Label? _companionHintLabel;

    public override void _Ready()
    {
        Title = "事件結算";
        MinSize = new Vector2I(540, 480);
        Borderless = false;

        // AcceptDialog 預設只有一個 OK 按鈕；移除 OK，自製 Roll + Confirm
        GetOkButton().Visible = false;

        var root = new VBoxContainer { CustomMinimumSize = new Vector2(520, 380) };
        root.AddThemeConstantOverride("separation", 12);
        AddChild(root);

        // === 頂部：Stat · TN ===
        _statTnLabel = MakeLabel("", 12, Palette.OrnamentInk);
        _statTnLabel.HorizontalAlignment = HorizontalAlignment.Right;
        root.AddChild(_statTnLabel);

        // === 敘事 ===
        var narrativeBox = MakePanel(Palette.PaperLight, Palette.Gold, accentLeft: true);
        root.AddChild(narrativeBox);
        _narrativeLabel = MakeLabel("", 13, Palette.Ink);
        _narrativeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _narrativeLabel.CustomMinimumSize = new Vector2(480, 80);
        narrativeBox.AddChild(_narrativeLabel);

        // === 夥伴提示（stub）===
        _companionHintBox = MakePanel(Palette.WithAlpha(Palette.Brown, 0.18f), Palette.Brown, accentLeft: true);
        _companionHintBox.Visible = false;
        root.AddChild(_companionHintBox);
        _companionHintLabel = MakeLabel("", 11, Palette.InkLight);
        _companionHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _companionHintLabel.CustomMinimumSize = new Vector2(480, 0);
        _companionHintBox.AddChild(_companionHintLabel);

        // === 骰子顯示 ===
        var diceBox = MakePanel(Palette.PaperDark, Palette.InkLight);
        root.AddChild(diceBox);
        _diceLabel = MakeLabel("尚未擲骰", 16, Palette.Ink, bold: true);
        _diceLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _diceLabel.CustomMinimumSize = new Vector2(0, 50);
        diceBox.AddChild(_diceLabel);

        // === 結果 ===
        var outcomeBox = MakePanel(Palette.PaperLight, Palette.Brown);
        root.AddChild(outcomeBox);
        var outcomeVbox = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        outcomeVbox.AddThemeConstantOverride("separation", 6);
        outcomeBox.AddChild(outcomeVbox);
        _outcomeTierLabel = MakeLabel("", 14, Palette.Ink, bold: true);
        outcomeVbox.AddChild(_outcomeTierLabel);
        _outcomeNarrativeLabel = MakeLabel("", 12, Palette.Ink);
        _outcomeNarrativeLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _outcomeNarrativeLabel.CustomMinimumSize = new Vector2(480, 60);
        outcomeVbox.AddChild(_outcomeNarrativeLabel);

        // === Footer：擲骰 + 確認 ===
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        footer.AddThemeConstantOverride("separation", 8);
        root.AddChild(footer);

        _rollButton = new Button
        {
            Text = "⚄ 擲骰",
            CustomMinimumSize = new Vector2(120, 36),
        };
        _rollButton.Pressed += OnRollPressed;
        footer.AddChild(_rollButton);

        _confirmButton = new Button
        {
            Text = "確認・繼續",
            CustomMinimumSize = new Vector2(120, 36),
            Disabled = true,
        };
        _confirmButton.Pressed += OnConfirmPressed;
        footer.AddChild(_confirmButton);
    }

    /// <summary>顯示對話框；填入事件並重置狀態。</summary>
    public void Open(EventInstance instance, NpcCompanion? companion = null)
    {
        _instance = instance;
        _hasRolled = false;

        Title = $"事件 · {instance.Card.Name}";
        if (_statTnLabel != null) _statTnLabel.Text = $"屬性 {StatGlyph(instance.Card.Stat)} · TN {instance.Card.Tn}";
        if (_narrativeLabel != null) _narrativeLabel.Text = instance.Card.Narrative;
        if (_diceLabel != null) _diceLabel.Text = "尚未擲骰";
        if (_outcomeTierLabel != null) _outcomeTierLabel.Text = "—";
        if (_outcomeNarrativeLabel != null) _outcomeNarrativeLabel.Text = "（擲骰後顯示結果）";
        if (_rollButton != null) _rollButton.Disabled = false;
        if (_confirmButton != null) _confirmButton.Disabled = true;

        UpdateCompanionHint(companion, instance.Card.Type);

        PopupCentered();
    }

    private void UpdateCompanionHint(NpcCompanion? companion, EventType type)
    {
        if (_companionHintBox is null || _companionHintLabel is null) return;
        if (companion is null)
        {
            _companionHintBox.Visible = false;
            return;
        }
        var scene = SceneFromEventType(type);
        string? hint = null;
        foreach (var resp in companion.SceneResponses)
        {
            if (string.Equals(resp.Scene, scene, System.StringComparison.OrdinalIgnoreCase))
            {
                hint = resp.Text;
                break;
            }
        }
        if (string.IsNullOrEmpty(hint) && !string.IsNullOrEmpty(companion.Personality))
            hint = companion.Personality;
        if (string.IsNullOrEmpty(hint))
        {
            _companionHintBox.Visible = false;
            return;
        }
        _companionHintLabel.Text = $"｛{companion.Name}｝{hint}";
        _companionHintBox.Visible = true;
    }

    private static string SceneFromEventType(EventType t) => t switch
    {
        EventType.Exploration => "exploration",
        EventType.Negotiation => "negotiation",
        EventType.Battle => "battle",
        EventType.Special => "special",
        _ => "default",
    };

    private void OnRollPressed()
    {
        if (_instance is null || _hasRolled) return;
        _hasRolled = true;

        // demo 用：玩家 Stat = 0（後續接 GameState 後改取真實角色 stat）
        const int statValue = 0;
        var roll = _dice.Roll2d6();
        int total = roll.Total + statValue;
        int tn = _instance.Card.Tn;

        _tier = total >= tn
            ? EventOutcomeTier.Success
            : (total >= tn - 2
                ? EventOutcomeTier.PartialSuccess
                : EventOutcomeTier.Failure);

        var outcome = _tier switch
        {
            EventOutcomeTier.Success => _instance.Card.Outcomes.Success,
            EventOutcomeTier.PartialSuccess => _instance.Card.Outcomes.PartialSuccess,
            _ => _instance.Card.Outcomes.Failure,
        };

        if (_diceLabel != null)
            _diceLabel.Text = $"d6 + d6 + Stat = {roll.D1} + {roll.D2} + {statValue} = {total}    vs TN {tn}";
        if (_outcomeTierLabel != null)
        {
            _outcomeTierLabel.Text = TierLabel(_tier);
            _outcomeTierLabel.AddThemeColorOverride("font_color", TierColor(_tier));
        }
        if (_outcomeNarrativeLabel != null) _outcomeNarrativeLabel.Text = outcome.Narrative;
        if (_rollButton != null) _rollButton.Disabled = true;
        if (_confirmButton != null) _confirmButton.Disabled = false;
    }

    private void OnConfirmPressed()
    {
        if (_instance is null || !_hasRolled) return;
        EmitSignal(SignalName.EventResolved, _instance.Card.Id, (int)_tier);
        Hide();
    }

    private static string TierLabel(EventOutcomeTier tier) => tier switch
    {
        EventOutcomeTier.Success => "✓ 成功",
        EventOutcomeTier.PartialSuccess => "◐ 部分成功",
        EventOutcomeTier.Failure => "✗ 失敗",
        _ => "?",
    };

    private static Color TierColor(EventOutcomeTier tier) => tier switch
    {
        EventOutcomeTier.Success => Palette.Green,
        EventOutcomeTier.PartialSuccess => Palette.Gold,
        EventOutcomeTier.Failure => Palette.RedDark,
        _ => Palette.Ink,
    };

    private static string StatGlyph(Stat s) => s switch
    {
        Stat.Power => "武",
        Stat.Social => "社",
        Stat.Skill => "技",
        Stat.Intellect => "智",
        _ => "",
    };

    private static PanelContainer MakePanel(Color bg, Color border, bool accentLeft = false)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = accentLeft ? 4 : 1,
            BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        return p;
    }

    private static Label MakeLabel(string text, int size, Color color, bool bold = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }
}
