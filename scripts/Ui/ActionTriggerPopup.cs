using Godot;

namespace HauntedManor.Scripts.Ui;

/// <summary>
/// Phase 1 Task 5 — 玩家行動觸發器浮動視窗（規格書 §3.1.4 / §4.1 #12 / §4.2.3）。
/// 由 MainMapRenderer 在玩家點擊角色所在格時 ShowAt(...) 顯示；
/// 點觸發器以外任意處關閉（規格書 §3.1.4 關閉規則）。
/// </summary>
public partial class ActionTriggerPopup : PanelContainer
{
    [Signal] public delegate void MoveSelectedEventHandler();
    [Signal] public delegate void RestSelectedEventHandler();
    [Signal] public delegate void ObserveSelectedEventHandler();
    [Signal] public delegate void TalkSelectedEventHandler();

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = Control.MouseFilterEnum.Stop; // 確保自身吸收點擊，避免又被當外部點擊
        // 蓋過 ParallaxScene (ZIndex=5)：行動選單必須畫在立繪盒上面，否則會被遮住。
        ZIndex = 20;
        ZAsRelative = false;
        SetupButton("VBox/MoveButton", () => EmitSignal(SignalName.MoveSelected));
        SetupButton("VBox/RestButton", () => EmitSignal(SignalName.RestSelected));
        SetupButton("VBox/ObserveButton", () => EmitSignal(SignalName.ObserveSelected));
        SetupButton("VBox/TalkButton", () => EmitSignal(SignalName.TalkSelected));
    }

    private void SetupButton(string path, System.Action onPressed)
    {
        var btn = GetNodeOrNull<Button>(path);
        if (btn is null)
        {
            GD.PrintErr($"[ActionTriggerPopup] 找不到按鈕: {path}");
            return;
        }
        btn.Pressed += () =>
        {
            onPressed();
            HidePopup();
        };
    }

    /// <summary>規格書 §4.1 #12「角色所在格附近彈出」。</summary>
    public void ShowAt(Vector2 anchorScreenPos)
    {
        Visible = true;
        // 預設 popup 在 anchor 右下方略微偏移
        Position = anchorScreenPos + new Vector2(40, -10);
    }

    public void HidePopup()
    {
        Visible = false;
    }
}
