using Godot;

namespace HauntedManor.Scripts.Theme;

/// <summary>
/// 14 色羊皮紙 palette（規格書 §6.1 + Game UI Prototype.html）。
/// 統一所有 UI 顏色來源，避免散落 hex 字串。
/// 命名為 Palette 而非 Colors，避免與 Godot.Colors 衝突。
/// </summary>
public static class Palette
{
    // === 紙色系 ===
    public static readonly Color Paper      = Color.Color8(0xef, 0xe2, 0xc2); // 主紙色
    public static readonly Color PaperLight = Color.Color8(0xf7, 0xec, 0xd3); // 淺紙色
    public static readonly Color PaperDark  = Color.Color8(0xd9, 0xc5, 0x97); // 深紙色（panel header）

    // === 墨色系 ===
    public static readonly Color Ink        = Color.Color8(0x2b, 0x1d, 0x0e); // 主墨色（文字/邊框）
    public static readonly Color InkLight   = Color.Color8(0x4a, 0x36, 0x22); // 次墨色（次要邊框）
    public static readonly Color OrnamentInk= Color.Color8(0x7a, 0x60, 0x40); // 裝飾文字色
    public static readonly Color Brown      = Color.Color8(0xb8, 0x9d, 0x66); // dashed border / scrollbar

    // === 屬性色標 ===
    public static readonly Color Red        = Color.Color8(0xc6, 0x38, 0x38); // 戰鬥屬 / current tile / HP fill
    public static readonly Color RedDark    = Color.Color8(0x7a, 0x1f, 0x1a); // primary button border
    public static readonly Color Green      = Color.Color8(0x3d, 0x5a, 0x3a); // 探索屬
    public static readonly Color Blue       = Color.Color8(0x2a, 0x68, 0x98); // 社交屬
    public static readonly Color Purple     = Color.Color8(0x6a, 0x3a, 0x8a); // 知識屬
    public static readonly Color Gold       = Color.Color8(0xd4, 0xa0, 0x2a); // 重要邊框 / AP pip / 金框

    // === 環境色 ===
    public static readonly Color Bg         = Color.Color8(0x0d, 0x0a, 0x07); // body bg (stage 外的舞台暗背景)

    // === 半透明 helpers ===
    public static Color WithAlpha(Color c, float a) => new(c.R, c.G, c.B, a);
}
