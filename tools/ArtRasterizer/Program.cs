// ArtRasterizer — 一次性 SkiaSharp 工具：把設計 bundle（Cards.jsx）的 SVG 繪圖邏輯
// port 成 Skia 指令直接輸出 PNG。對齊 §美術需求說明.md §1.2 調色盤 / §3 尺寸規格。
// 產出路徑：modules/builtin/abandoned-mansion/art/{characters,placeholders/*}/
using SkiaSharp;

namespace ArtRasterizer;

public static class Program
{
    // §1.2 調色盤
    static readonly SKColor Ink         = SKColor.Parse("#2b1d0e");
    static readonly SKColor Ink2        = SKColor.Parse("#4a3622");
    static readonly SKColor InkFaded    = SKColor.Parse("#7a6040");
    static readonly SKColor Paper       = SKColor.Parse("#efe2c2");
    static readonly SKColor Paper2      = SKColor.Parse("#f7ecd3");
    static readonly SKColor Paper3      = SKColor.Parse("#d9c597");
    static readonly SKColor PaperShadow = SKColor.Parse("#b89d66");
    static readonly SKColor Accent      = SKColor.Parse("#c63838");
    static readonly SKColor AccentSoft  = SKColor.Parse("#e4b2b2");
    static readonly SKColor Gold        = SKColor.Parse("#8e6c1a");
    static readonly SKColor Gob         = SKColor.Parse("#d4aa48");
    static readonly SKColor Crim        = SKColor.Parse("#7a1f1a");
    static readonly SKColor Fst         = SKColor.Parse("#3d5a3a");
    static readonly SKColor Blue        = SKColor.Parse("#3d5a7a");

    public static int Main(string[] args)
    {
        string repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string artRoot  = Path.Combine(repoRoot, "modules", "builtin", "abandoned-mansion", "art");
        Directory.CreateDirectory(Path.Combine(artRoot, "characters"));
        Directory.CreateDirectory(Path.Combine(artRoot, "placeholders", "action-cards"));
        Directory.CreateDirectory(Path.Combine(artRoot, "placeholders", "tiles"));
        Directory.CreateDirectory(Path.Combine(artRoot, "placeholders", "events"));
        Directory.CreateDirectory(Path.Combine(artRoot, "placeholders", "equipment"));

        int count = 0;
        foreach (var id in new[] { "scholar", "guard", "scout", "occultist" })
        {
            Render(Path.Combine(artRoot, "characters", $"{id}.full.png"),
                   180, 252, c => DrawCharacterFull(c, id)); count++;
        }
        foreach (var t in new[] { "thinking", "combat", "exploration", "communication" })
        {
            Render(Path.Combine(artRoot, "placeholders", "action-cards", $"{t}.full.png"),
                   180, 142, c => DrawActionIllust(c, t, 180, 142)); count++;
        }
        foreach (var t in new[] { "town", "wilderness", "dungeon", "special" })
        {
            Render(Path.Combine(artRoot, "placeholders", "tiles", $"{t}.full.png"),
                   220, 220, c => DrawTileIllust(c, t, 220, 220)); count++;
        }
        // B1 擴展 · events placeholders：復用 ActionIllust 對應類型（事件與行動共用視覺語彙）
        var eventMap = new (string file, string type)[]
        {
            ("exploration", "exploration"),
            ("negotiation", "communication"),
            ("battle",      "combat"),
            ("special",     "thinking"),
        };
        foreach (var (file, actionType) in eventMap)
        {
            Render(Path.Combine(artRoot, "placeholders", "events", $"{file}.full.png"),
                   180, 142, c => DrawActionIllust(c, actionType, 180, 142)); count++;
        }
        // B1 擴展 · equipment placeholders：Weapon / Armor / Accessory / Special
        foreach (var cat in new[] { "weapon", "armor", "accessory", "special" })
        {
            Render(Path.Combine(artRoot, "placeholders", "equipment", $"{cat}.full.png"),
                   180, 142, c => DrawEquipIllust(c, cat, 180, 142)); count++;
        }
        Console.WriteLine($"Wrote {count} PNG file(s) under {artRoot}");
        return 0;
    }

    // ── EQUIPMENT ILLUST（Cards.jsx:305-366）──────────────────────────────
    static void DrawEquipIllust(SKCanvas c, string category, int w, int h)
    {
        Fill(c, 0, 0, w, h, Paper.WithAlpha(115));
        using var weave = new SKPaint { Color = Ink.WithAlpha(15), Style = SKPaintStyle.Stroke, StrokeWidth = 0.25f };
        for (int i = 0; i < 7; i++) c.DrawLine(i * 30, 0, i * 30, h, weave);
        for (int i = 0; i < 5; i++) c.DrawLine(0, i * 34, w, i * 34, weave);

        switch (category)
        {
            case "weapon":    DrawEquipWeapon(c); break;
            case "armor":     DrawEquipArmor(c); break;
            case "accessory": DrawEquipAccessory(c); break;
            default:          DrawEquipSpecial(c); break;
        }
    }

    static void DrawEquipWeapon(SKCanvas c)
    {
        // revolver silhouette: body + cylinder + grip
        PolyFill(c, Ink2,
            (38, 95), (82, 48), (94, 58), (80, 72),
            (136, 72), (136, 92), (80, 92), (80, 104), (52, 104));
        using var outline = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        using var outlinePath = new SKPath();
        outlinePath.MoveTo(38, 95); outlinePath.LineTo(82, 48); outlinePath.LineTo(94, 58); outlinePath.LineTo(80, 72);
        outlinePath.LineTo(136, 72); outlinePath.LineTo(136, 92); outlinePath.LineTo(80, 92); outlinePath.LineTo(80, 104); outlinePath.LineTo(52, 104); outlinePath.Close();
        c.DrawPath(outlinePath, outline);
        using var circle = new SKPaint { Color = Ink2, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, IsAntialias = true };
        c.DrawCircle(134, 62, 7, circle);
        Rect(c, 80, 74, 12, 7, Ink.WithAlpha(153));
        Ellipse(c, 90, 118, 42, 8, Ink.WithAlpha(25));
        Line(c, 18, 110, 162, 110, Ink2.WithAlpha(102), 0.8f);
    }

    static void DrawEquipArmor(SKCanvas c)
    {
        using var p = new SKPath();
        p.MoveTo(48, 135); p.LineTo(43, 52); p.QuadTo(65, 36, 90, 34); p.QuadTo(115, 36, 137, 52); p.LineTo(132, 135); p.Close();
        using var fp = new SKPaint { Color = Ink2, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var sp = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        c.DrawPath(p, fp); c.DrawPath(p, sp);
        using var dash = SKPathEffect.CreateDash(new[] { 3f, 2.5f }, 0);
        using var dashPaint = new SKPaint { Color = Paper3, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, PathEffect = dash, IsAntialias = true };
        c.DrawLine(90, 34, 90, 94, dashPaint);
        using var curve = new SKPaint { Color = Paper3, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f, IsAntialias = true };
        using var lc = new SKPath(); lc.MoveTo(43, 72); lc.QuadTo(60, 66, 78, 72);
        using var rc = new SKPath(); rc.MoveTo(137, 72); rc.QuadTo(120, 66, 102, 72);
        c.DrawPath(lc, curve); c.DrawPath(rc, curve);
        foreach (var y in new[] { 52, 68, 84, 100 })
            Circle(c, 90, y, 3.5f, Gob, Ink, 0.8f);
        Ellipse(c, 90, 140, 44, 7, Ink.WithAlpha(25));
    }

    static void DrawEquipAccessory(SKCanvas c)
    {
        // pocket watch
        Circle(c, 90, 76, 40, Paper2, Ink2, 1.8f);
        Circle(c, 90, 76, 31, Paper3, Ink, 1.5f);
        Circle(c, 90, 76, 2.5f, Ink);
        Line(c, 90, 76, 90, 49, Ink, 1.8f);
        Line(c, 90, 76, 110, 82, Ink, 1.2f);
        for (int deg = 0; deg < 360; deg += 30)
        {
            double r = deg * Math.PI / 180;
            Line(c, 90 + (float)(26 * Math.Cos(r)), 76 + (float)(26 * Math.Sin(r)),
                    90 + (float)(29 * Math.Cos(r)), 76 + (float)(29 * Math.Sin(r)), Ink2, 1.2f);
        }
        using var chain = SKPathEffect.CreateDash(new[] { 3f, 1.5f }, 0);
        using var chainPaint = new SKPaint { Color = Gob, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, PathEffect = chain, IsAntialias = true };
        using var cp = new SKPath(); cp.MoveTo(90, 36); cp.QuadTo(90, 24, 96, 18); cp.QuadTo(102, 13, 108, 16);
        c.DrawPath(cp, chainPaint);
        Circle(c, 90, 36, 3.5f, Gob, Ink, 0.8f);
        Ellipse(c, 90, 124, 38, 7, Ink.WithAlpha(25));
    }

    static void DrawEquipSpecial(SKCanvas c)
    {
        // journal / notebook
        Rect(c, 40, 24, 100, 108, SKColor.Parse("#6a5030"), Ink, 2, 3);
        Rect(c, 46, 30, 88, 96, Paper, Ink2, 0.8f, 2);
        using var border = new SKPaint { Color = Gob.WithAlpha(166), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
        c.DrawRect(new SKRect(52, 36, 128, 120), border);
        // center gold cross
        Line(c, 90, 60, 90, 104, Gob.WithAlpha(166), 2);
        Line(c, 68, 82, 112, 82, Gob.WithAlpha(166), 2);
        Circle(c, 90, 82, 12, Gob.WithAlpha(80));
        Line(c, 58, 52, 122, 52, Ink2, 0.8f);
        Line(c, 62, 60, 118, 60, Ink2, 0.6f);
        Rect(c, 136, 70, 12, 20, Gob, Ink, 1, 2);
        Ellipse(c, 90, 138, 48, 7, Ink.WithAlpha(25));
    }

    static void Render(string path, int w, int h, Action<SKCanvas> draw)
    {
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        draw(canvas);
        canvas.Flush();
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 92);
        using var fs = File.Create(path);
        data.SaveTo(fs);
        Console.WriteLine($"  {Path.GetFileName(path)}  {w}×{h}");
    }

    // ── ACTION ILLUST（Cards.jsx:48-127 · 佔位，type-level 共享圖）─────────
    static void DrawActionIllust(SKCanvas c, string type, int w, int h)
    {
        // background wash
        Fill(c, 0, 0, w, h, Paper3.WithAlpha(97));
        // vertical rule lines (very faint parchment weave)
        using var rule = new SKPaint { Color = Ink.WithAlpha(15), Style = SKPaintStyle.Stroke, StrokeWidth = 0.3f };
        for (int i = 0; i < 12; i++)
            c.DrawLine(i * 18 - 10, 0, i * 18 + 20, h, rule);

        switch (type)
        {
            case "thinking":      DrawActionThinking(c); break;
            case "combat":        DrawActionCombat(c); break;
            case "exploration":   DrawActionExploration(c); break;
            case "communication": DrawActionCommunication(c); break;
        }
        // §1.3 · PNG 內不放文字；類型徽章由 UI 層渲染。
    }

    static void DrawActionThinking(SKCanvas c)
    {
        // two book pages
        Rect(c, 48, 18, 38, 54, Paper3, Ink2, 1.2f);
        Rect(c, 86, 18, 38, 54, Paper2, Ink2, 1.2f);
        Line(c, 86, 20, 86, 70, Ink, 0.8f);
        using var dashFx = SKPathEffect.CreateDash(new[] { 3f, 1.5f }, 0);
        using var inkDashed = new SKPaint { Color = Ink2, Style = SKPaintStyle.Stroke, StrokeWidth = 0.7f, PathEffect = dashFx, IsAntialias = true };
        foreach (var y in new[] { 28, 36, 44, 52, 60 })
        {
            c.DrawLine(53, y, 80, y, inkDashed);
            c.DrawLine(91, y, 118, y, inkDashed);
        }
        // candle glow halo
        Circle(c, 145, 100, 26, Gob.WithAlpha(31));
        Ellipse(c, 145, 100, 14, 18, Gob.WithAlpha(38));
        // candle stick
        Rect(c, 141, 78, 7, 24, Paper3, Ink2, 0.8f);
        // flame
        Ellipse(c, 144, 76, 4, 6, Gob.WithAlpha(191));
        Ellipse(c, 144, 73, 2.5f, 4, SKColor.Parse("#fff8d0").WithAlpha(230));
        // ink well
        Rect(c, 28, 82, 26, 20, Ink2, Ink, 1, 4);
        Ellipse(c, 41, 82, 13, 5, Ink2, Ink, 0.8f);
        // quill
        using var quill = new SKPaint { Color = Paper3, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(55, 92); path.QuadTo(47, 75, 60, 55); path.QuadTo(68, 43, 51, 28);
        c.DrawPath(path, quill);
    }

    static void DrawActionCombat(SKCanvas c)
    {
        // X-shape crossed weapons
        using var thick = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 3, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        using var mid = new SKPaint { Color = Ink2, Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        c.DrawLine(38, 28, 142, 114, thick);
        c.DrawLine(142, 28, 38, 114, mid);
        // central crossing halo
        Circle(c, 90, 71, 22, Accent.WithAlpha(25));
        Circle(c, 90, 71, 12, Accent.WithAlpha(25));
        // rivet corners
        foreach (var (x, y) in new[] { (38, 28), (142, 28), (38, 114), (142, 114) })
        {
            Circle(c, x, y, 6, PaperShadow, Ink, 1);
            Circle(c, x, y, 2, Ink);
        }
        // motion arc
        using var arc = new SKPaint { Color = Crim.WithAlpha(102), Style = SKPaintStyle.Stroke, StrokeWidth = 1, PathEffect = SKPathEffect.CreateDash(new[] { 2f, 2f }, 0), IsAntialias = true };
        using var arcP = new SKPath(); arcP.MoveTo(60, 108); arcP.QuadTo(80, 95, 110, 100); arcP.QuadTo(130, 102, 145, 90);
        c.DrawPath(arcP, arc);
    }

    static void DrawActionExploration(SKCanvas c)
    {
        // compass
        using var stroke = new SKPaint { Color = Ink2.WithAlpha(102), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
        c.DrawCircle(90, 72, 38, stroke);
        Circle(c, 90, 72, 30, Paper3.WithAlpha(153), Ink, 1.2f);
        Line(c, 90, 38, 90, 106, Ink, 1);
        Line(c, 56, 72, 124, 72, Ink, 1);
        // N arrow (red), S, E, W arrows
        PolyFill(c, Crim, (90,41),(86,55),(90,52),(94,55));
        PolyFill(c, Ink2, (90,103),(86,89),(90,92),(94,89));
        PolyFill(c, Ink2, (121,72),(107,68),(110,72),(107,76));
        PolyFill(c, Ink2, (59,72),(73,68),(70,72),(73,76));
        Circle(c, 90, 72, 4, Gob, Ink, 0.8f);
        // leaf marks
        Ellipse(c, 152, 98, 5, 8, Ink2.WithAlpha(89), rotDeg: -20);
        Ellipse(c, 162, 88, 4, 7, Ink2.WithAlpha(89), rotDeg: 20);
        Ellipse(c, 168, 105, 5, 8, Ink2.WithAlpha(89), rotDeg: -15);
        // trail
        using var trail = new SKPaint { Color = Fst.WithAlpha(127), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, PathEffect = SKPathEffect.CreateDash(new[] { 3f, 2f }, 0), IsAntialias = true };
        using var tp = new SKPath(); tp.MoveTo(18, 125); tp.QuadTo(40, 108, 65, 115); tp.QuadTo(82, 120, 90, 90);
        c.DrawPath(tp, trail);
    }

    static void DrawActionCommunication(SKCanvas c)
    {
        // speech bubble
        using var bubble = new SKPath();
        bubble.MoveTo(30, 25); bubble.QuadTo(30, 20, 36, 20);
        bubble.LineTo(148, 20); bubble.QuadTo(154, 20, 154, 26);
        bubble.LineTo(154, 82); bubble.QuadTo(154, 88, 148, 88);
        bubble.LineTo(72, 88); bubble.LineTo(56, 110); bubble.LineTo(60, 88);
        bubble.LineTo(36, 88); bubble.QuadTo(30, 88, 30, 82); bubble.Close();
        using var pf = new SKPaint { Color = Paper2, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var ps = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f, IsAntialias = true };
        c.DrawPath(bubble, pf); c.DrawPath(bubble, ps);
        // text lines in bubble
        using var dash = new SKPaint { Color = Ink2, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, PathEffect = SKPathEffect.CreateDash(new[] { 4f, 1.5f }, 0), IsAntialias = true };
        foreach (var y in new[] { 36, 46, 56, 66 })
            c.DrawLine(46, y, 138, y, dash);
        // wax seal (no glyph; UI layer adds iconography)
        Circle(c, 148, 118, 15, Accent.WithAlpha(166), Crim, 1.2f);
        Circle(c, 148, 118, 5, Paper2.WithAlpha(217));
        // quill feather
        using var qp = new SKPath(); qp.MoveTo(28, 112); qp.QuadTo(16, 96, 24, 74); qp.QuadTo(32, 55, 22, 36);
        using var qs = new SKPaint { Color = Paper2.WithAlpha(217), Style = SKPaintStyle.Stroke, StrokeWidth = 2, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        c.DrawPath(qp, qs);
    }

    // ── TILE ILLUST（Cards.jsx:207-302）────────────────────────────────────
    static void DrawTileIllust(SKCanvas c, string terrain, int w, int h)
    {
        SKColor bg = terrain switch
        {
            "town" => SKColor.Parse("#c9a96a"),
            "wilderness" => SKColor.Parse("#7a9e6a"),
            "dungeon" => SKColor.Parse("#8a7860"),
            _ => SKColor.Parse("#7070a8"),
        };
        Fill(c, 0, 0, w, h, bg.WithAlpha(115));
        // vw,vh=220,148 in bundle; we upscale to fill 220×220 by stretching Y 220/148.
        c.Save();
        c.Scale(1f, h / 148f);
        switch (terrain)
        {
            case "town": DrawTileTown(c); break;
            case "wilderness": DrawTileWilderness(c); break;
            case "dungeon": DrawTileDungeon(c); break;
            default: DrawTileSpecial(c); break;
        }
        c.Restore();
    }

    static void DrawTileTown(SKCanvas c)
    {
        // cobblestone grid
        for (int r = 0; r < 5; r++)
        for (int cc = 0; cc < 7; cc++)
        {
            var col = (cc % 2 == r % 2) ? SKColor.Parse("#c8a870") : SKColor.Parse("#b89060");
            Rect(c, cc * 32 + 2, r * 26 + 14, 28, 22, col.WithAlpha(140), Ink2, 0.5f, 2);
        }
        // main house
        Rect(c, 35, 20, 58, 85, SKColor.Parse("#c8a870"), Ink, 1.5f);
        PolyFill(c, SKColor.Parse("#b08a50"), (35,20),(93,20),(64,2));
        PolyStroke(c, Ink, 1.5f, (35,20),(93,20),(64,2),(35,20));
        // door
        Rect(c, 53, 58, 20, 30, Ink2, Ink, 1);
        // windows
        Rect(c, 41, 35, 16, 15, Blue.WithAlpha(140), Ink, 0.8f);
        Rect(c, 69, 35, 16, 15, Blue.WithAlpha(140), Ink, 0.8f);
        // second building
        Rect(c, 120, 32, 72, 68, SKColor.Parse("#ceb488"), Ink, 1.5f);
        PolyFill(c, SKColor.Parse("#b08a50"), (120,32),(192,32),(156,10));
        PolyStroke(c, Ink, 1.5f, (120,32),(192,32),(156,10),(120,32));
        Rect(c, 148, 56, 18, 26, Ink2, Ink, 1);
        Rect(c, 126, 44, 13, 13, Blue.WithAlpha(127), Ink, 0.7f);
        Rect(c, 170, 44, 13, 13, Blue.WithAlpha(127), Ink, 0.7f);
        // church belltower
        Rect(c, 152, 0, 10, 16, SKColor.Parse("#b08a50"), Ink, 1);
        PolyFill(c, Ink, (152,0),(162,0),(157,-9));
        // square well
        using var sp = new SKPaint { Color = Ink2, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        c.DrawCircle(108, 118, 13, sp);
        Line(c, 95, 110, 121, 110, Ink2, 1.5f);
        Line(c, 108, 104, 108, 108, Ink2, 1.5f);
    }

    static void DrawTileWilderness(SKCanvas c)
    {
        // grass band
        Fill(c, 0, 85, 220, 63, SKColor.Parse("#6a9460").WithAlpha(127));
        // winding path
        using var path = new SKPath(); path.MoveTo(0, 148); path.QuadTo(55, 128, 85, 118); path.QuadTo(125, 104, 162, 114); path.QuadTo(192, 122, 220, 108);
        using var sp = new SKPaint { Color = SKColor.Parse("#c8a870").WithAlpha(166), Style = SKPaintStyle.Stroke, StrokeWidth = 10, IsAntialias = true };
        c.DrawPath(path, sp);
        // trees
        var trees = new (int x, int y)[] { (28, 62), (72, 47), (128, 57), (172, 42), (204, 68) };
        for (int i = 0; i < trees.Length; i++)
        {
            var (x, y) = trees[i];
            Rect(c, x - 3, y + 28, 6, 22, SKColor.Parse("#6b4020"), Ink2, 0.8f);
            var leaf = i % 2 == 0 ? SKColor.Parse("#4a6e3a") : SKColor.Parse("#3a5a2a");
            Circle(c, x, y + 14, 24, leaf, Ink2, 1);
            Circle(c, x - 9, y + 22, 15, SKColor.Parse("#5a7e4a").WithAlpha(166));
        }
        // horizon haze
        Fill(c, 0, 58, 220, 28, Fst.WithAlpha(71));
        Fill(c, 0, 52, 220, 18, Paper2.WithAlpha(46));
        // grass tufts
        foreach (var x in new[] { 18, 48, 88, 135, 175, 208 })
            Ellipse(c, x, 145, 5, 2, SKColor.Parse("#4a6030").WithAlpha(102));
    }

    static void DrawTileDungeon(SKCanvas c)
    {
        // stone tiles
        for (int r = 0; r < 5; r++)
        for (int cc = 0; cc < 7; cc++)
        {
            var col = (r % 2 == cc % 2) ? SKColor.Parse("#8a7860") : SKColor.Parse("#7a6850");
            Rect(c, cc * 32, r * 26 + 20, 30, 24, col.WithAlpha(166), Ink, 0.8f, 1);
        }
        // archway
        using var outer = new SKPath(); outer.MoveTo(58, 148); outer.LineTo(58, 52); outer.QuadTo(110, 16, 162, 52); outer.LineTo(162, 148);
        using var fo = new SKPaint { Color = SKColor.Parse("#524230"), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var so = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
        c.DrawPath(outer, fo); c.DrawPath(outer, so);
        using var inner = new SKPath(); inner.MoveTo(68, 148); inner.LineTo(68, 58); inner.QuadTo(110, 26, 152, 58); inner.LineTo(152, 148);
        using var fi = new SKPaint { Color = SKColor.Parse("#1a1208"), Style = SKPaintStyle.Fill, IsAntialias = true };
        c.DrawPath(inner, fi);
        // torches
        foreach (var (x, y) in new[] { (38, 82), (182, 82) })
        {
            Rect(c, x - 4, y, 8, 16, Paper3, Ink2, 0.8f, 1);
            Ellipse(c, x, y - 2, 5, 7, Gob.WithAlpha(166));
            Ellipse(c, x, y - 5, 2.5f, 4, SKColor.Parse("#fff8d0").WithAlpha(217));
            Ellipse(c, x, y + 6, 18, 9, Gob.WithAlpha(20));
        }
        // dust particles
        foreach (var (x, y) in new[] { (105, 38), (152, 52), (82, 28), (175, 32) })
            Circle(c, x, y, 1.5f, Paper3.WithAlpha(97));
    }

    static void DrawTileSpecial(SKCanvas c)
    {
        Fill(c, 0, 0, 220, 148, SKColor.Parse("#0a0a22").WithAlpha(166));
        // ritual rings
        for (int i = 0; i < 4; i++)
        {
            float r = new[] { 52f, 40, 28, 16 }[i] * 1.85f;
            using var p = new SKPaint { Color = SKColor.Parse("#6040a0").WithAlpha((byte)(71 + i * 25)), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, PathEffect = SKPathEffect.CreateDash(new[] { i * 3f + 2, 2 }, 0), IsAntialias = true };
            c.DrawCircle(110, 80, r, p);
        }
        // glyph dots – stand-ins for mystical marks (§1.3 無 PNG 內文字)
        using var gp = new SKPaint { Color = SKColor.Parse("#8060c0").WithAlpha(140), Style = SKPaintStyle.Fill, IsAntialias = true };
        using var gs = new SKPaint { Color = SKColor.Parse("#b0a0e0").WithAlpha(170), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
        foreach (var (x, y) in new[] { (72, 52), (148, 52), (110, 112), (78, 92), (142, 92) })
        {
            c.DrawCircle(x, y, 4, gp);
            c.DrawCircle(x, y, 6, gs);
        }
        // core
        Circle(c, 110, 80, 22, SKColor.Parse("#6040a0").WithAlpha(71));
        Circle(c, 110, 80, 12, SKColor.Parse("#9060e0").WithAlpha(97));
        Circle(c, 110, 80, 6, SKColor.Parse("#c0a0ff").WithAlpha(148));
        // radial rays
        using var rp = new SKPaint { Color = SKColor.Parse("#6040a0").WithAlpha(71), Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f, IsAntialias = true };
        for (int i = 0; i < 8; i++)
        {
            double a = i * 45 * Math.PI / 180.0;
            c.DrawLine(110f, 80f, 110f + (float)(Math.Cos(a) * 52), 80f + (float)(Math.Sin(a) * 52), rp);
        }
    }

    // ── CHARACTER FULL CARD（Cards.jsx:470-486 · 180×252）─────────────────
    static void DrawCharacterFull(SKCanvas c, string id)
    {
        // frame bg = dark navy
        Fill(c, 0, 0, 180, 252, SKColor.Parse("#0c1828"));
        // portrait zone 180×200
        c.Save();
        c.ClipRect(new SKRect(0, 0, 180, 200));
        DrawCharPortrait(c, id);
        c.Restore();
        // bottom name strip 180×50
        using var grad = SKShader.CreateLinearGradient(
            new SKPoint(0, 200), new SKPoint(0, 252),
            new[] { SKColor.Parse("#0e1a2e"), SKColor.Parse("#162030") },
            null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = grad };
        c.DrawRect(new SKRect(0, 200, 180, 252), paint);
        // divider 1px line above strip
        Line(c, 0, 200, 180, 200, Ink, 1);

        // §1.3 · PNG 內不放文字，姓名/稱號由 UI 層渲染。
        // 保留下方 name-strip 作為裝飾襯底（程式繪的文字會蓋在上面）。

        // frame outline
        using var border = new SKPaint { Color = Ink, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        c.DrawRect(new SKRect(0.75f, 0.75f, 179.25f, 251.25f), border);
    }

    // Painterly impressionist portrait – 4 unique configurations (Cards.jsx:130-204).
    static void DrawCharPortrait(SKCanvas c, string id)
    {
        // Base dark wash
        SKColor basec = id switch
        {
            "scholar" => SKColor.Parse("#0c1828"),
            "guard" => SKColor.Parse("#160808"),
            "scout" => SKColor.Parse("#081210"),
            "occultist" => SKColor.Parse("#060618"),
            _ => SKColor.Parse("#0c1828"),
        };
        Fill(c, 0, 0, 180, 200, basec);

        // Blob layer (heavy blur for painterly feel)
        var layer = SKSurface.Create(new SKImageInfo(180, 200, SKColorType.Rgba8888, SKAlphaType.Premul));
        var lc = layer.Canvas;
        lc.Clear(SKColors.Transparent);

        var blobs = id switch
        {
            "scholar" => new (float cx, float cy, float rx, float ry, string col, float opa)[]
            {
                (45, 170, 90, 85, "#f5c430", 0.52f),
                (135, 55, 72, 95, "#30a0c8", 0.48f),
                (18, 95, 55, 65, "#e060a0", 0.22f),
                (125, 160, 65, 55, "#2d4870", 0.50f),
            },
            "guard" => new (float, float, float, float, string, float)[]
            {
                (28, 185, 95, 85, "#c63838", 0.52f),
                (148, 75, 65, 75, "#f5c430", 0.28f),
                (80, 50, 85, 65, "#4a1808", 0.70f),
                (155, 175, 55, 55, "#7a1f1a", 0.45f),
            },
            "scout" => new (float, float, float, float, string, float)[]
            {
                (48, 175, 88, 82, "#3d7a3a", 0.68f),
                (138, 65, 72, 85, "#f5c430", 0.38f),
                (8, 75, 55, 75, "#30a0c8", 0.28f),
                (130, 158, 65, 65, "#8e6c1a", 0.32f),
            },
            "occultist" => new (float, float, float, float, string, float)[]
            {
                (80, 115, 95, 95, "#5040a0", 0.58f),
                (28, 45, 65, 72, "#30a0c8", 0.48f),
                (148, 165, 72, 65, "#e060a0", 0.32f),
                (140, 42, 55, 55, "#f5c430", 0.28f),
            },
            _ => Array.Empty<(float, float, float, float, string, float)>(),
        };

        foreach (var (cx, cy, rx, ry, col, opa) in blobs)
        {
            using var pp = new SKPaint
            {
                Color = SKColor.Parse(col).WithAlpha((byte)(opa * 255)),
                Style = SKPaintStyle.Fill,
                IsAntialias = true,
            };
            lc.DrawOval(cx, cy, rx, ry, pp);
        }
        layer.Canvas.Flush();

        // Composite blob layer with heavy blur for impressionist softness.
        using (var blur = SKImageFilter.CreateBlur(16, 16))
        using (var blurPaint = new SKPaint { ImageFilter = blur })
        {
            using var snap = layer.Snapshot();
            c.DrawImage(snap, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), blurPaint);
        }
        layer.Dispose();

        // Figure silhouette (shoulders + head suggestion)
        SKColor shadow = id switch
        {
            "scholar" => SKColor.Parse("#081024").WithAlpha(191),
            "guard" => SKColor.Parse("#120404").WithAlpha(184),
            "scout" => SKColor.Parse("#040e08").WithAlpha(178),
            "occultist" => SKColor.Parse("#040414").WithAlpha(166),
            _ => SKColors.Black.WithAlpha(160),
        };
        using var figPath = new SKPath();
        // Shared figure shape (tall oval-like), offset per character for slight variation.
        float dx = id switch { "guard" => -4, "scout" => -2, "occultist" => 0, _ => 0 };
        float topY = id switch { "guard" => 48, "scout" => 60, "occultist" => 50, _ => 62 };
        figPath.MoveTo(90 + dx, 200);
        figPath.QuadTo(62 + dx, 185, 57 + dx, 158);
        figPath.QuadTo(52 + dx, 135, 60 + dx, 118);
        figPath.QuadTo(55 + dx, 105, 58 + dx, topY + 28);
        figPath.QuadTo(61 + dx, topY + 14, 70 + dx, topY + 8);
        figPath.QuadTo(79 + dx, topY, 90 + dx, topY);
        figPath.QuadTo(101 + dx, topY, 110 + dx, topY + 8);
        figPath.QuadTo(119 + dx, topY + 14, 122 + dx, topY + 28);
        figPath.QuadTo(125 + dx, 105, 120 + dx, 118);
        figPath.QuadTo(128 + dx, 135, 123 + dx, 158);
        figPath.QuadTo(118 + dx, 185, 90 + dx, 200);
        figPath.Close();
        using var figP = new SKPaint { Color = shadow, Style = SKPaintStyle.Fill, IsAntialias = true };
        c.DrawPath(figPath, figP);

        // Painterly texture stripes
        using var texture = new SKPaint { Color = SKColors.White.WithAlpha(8), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
        for (int i = 0; i < 10; i++)
            c.DrawLine(0, i * 22, 180, i * 22 + 8, texture);
    }

    // ── low-level helpers ─────────────────────────────────────────────────
    static void Fill(SKCanvas c, float x, float y, float w, float h, SKColor col)
    {
        using var p = new SKPaint { Color = col, Style = SKPaintStyle.Fill };
        c.DrawRect(new SKRect(x, y, x + w, y + h), p);
    }

    static void Rect(SKCanvas c, float x, float y, float w, float h, SKColor fill, SKColor stroke, float sw, float r = 0)
    {
        var rect = new SKRect(x, y, x + w, y + h);
        using var fp = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var sp = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = sw, IsAntialias = true };
        if (r > 0) { c.DrawRoundRect(rect, r, r, fp); c.DrawRoundRect(rect, r, r, sp); }
        else       { c.DrawRect(rect, fp);            c.DrawRect(rect, sp); }
    }
    static void Rect(SKCanvas c, float x, float y, float w, float h, SKColor fill)
    {
        using var fp = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        c.DrawRect(new SKRect(x, y, x + w, y + h), fp);
    }

    static void Circle(SKCanvas c, float cx, float cy, float r, SKColor fill)
    {
        using var p = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        c.DrawCircle(cx, cy, r, p);
    }
    static void Circle(SKCanvas c, float cx, float cy, float r, SKColor fill, SKColor stroke, float sw)
    {
        using var fp = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var sp = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = sw, IsAntialias = true };
        c.DrawCircle(cx, cy, r, fp);
        c.DrawCircle(cx, cy, r, sp);
    }

    static void Ellipse(SKCanvas c, float cx, float cy, float rx, float ry, SKColor fill, float rotDeg = 0)
    {
        c.Save();
        if (rotDeg != 0) { c.Translate(cx, cy); c.RotateDegrees(rotDeg); c.Translate(-cx, -cy); }
        using var p = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        c.DrawOval(cx, cy, rx, ry, p);
        c.Restore();
    }
    static void Ellipse(SKCanvas c, float cx, float cy, float rx, float ry, SKColor fill, SKColor stroke, float sw)
    {
        using var fp = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var sp = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = sw, IsAntialias = true };
        c.DrawOval(cx, cy, rx, ry, fp);
        c.DrawOval(cx, cy, rx, ry, sp);
    }

    static void Line(SKCanvas c, float x0, float y0, float x1, float y1, SKColor col, float sw)
    {
        using var p = new SKPaint { Color = col, Style = SKPaintStyle.Stroke, StrokeWidth = sw, IsAntialias = true };
        c.DrawLine(x0, y0, x1, y1, p);
    }

    static void PolyFill(SKCanvas c, SKColor fill, params (float x, float y)[] points)
    {
        if (points.Length == 0) return;
        using var path = new SKPath();
        path.MoveTo(points[0].x, points[0].y);
        for (int i = 1; i < points.Length; i++) path.LineTo(points[i].x, points[i].y);
        path.Close();
        using var p = new SKPaint { Color = fill, Style = SKPaintStyle.Fill, IsAntialias = true };
        c.DrawPath(path, p);
    }

    static void PolyStroke(SKCanvas c, SKColor stroke, float sw, params (float x, float y)[] points)
    {
        if (points.Length == 0) return;
        using var path = new SKPath();
        path.MoveTo(points[0].x, points[0].y);
        for (int i = 1; i < points.Length; i++) path.LineTo(points[i].x, points[i].y);
        using var p = new SKPaint { Color = stroke, Style = SKPaintStyle.Stroke, StrokeWidth = sw, IsAntialias = true };
        c.DrawPath(path, p);
    }
}
