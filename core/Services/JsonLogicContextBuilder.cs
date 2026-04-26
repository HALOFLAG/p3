// JsonLogicContextBuilder — 從 GameState + Module + EventOrbit 組出 JsonLogic 變數命名空間。
//
// 規格書 §5.3 / §7.3 列出 8 類變數：
//   flag.<name>、hero.{hp,ap,attr.*}、companion[i].{hp,id}、companion.count、
//   turn、currentTile.{terrain,row,col,tileCardId}、
//   orbit.{A,B,C}.count、orbit.contains.<eventId>、
//   worldFlags.<name>
//
// 設計選擇：
//   - companion[i] 的索引語意以「state.Companions」順序為準（i 從 0 起）。
//     展平命名為 companion.0.hp / companion.1.hp …，符合 JsonLogic var 的 dot-segment 取值。
//   - orbit.contains.<eventId> 採布林 flag 寫法（只放在 orbit 上的 id 為 true，未列入即視為缺值 = false）。
//   - tile[r,c].* lazy resolver 不在本 helper 範圍（規格 §1.6 變化頻繁，留待專屬 PR）。
//   - 不存在的角色 / 同伴欄位完全跳過，避免「null var」誤觸發 JsonLogic 預設行為。
using CardNarrative.Core.Events;
using CardNarrative.Core.Models;
using CardNarrative.Core.State;

namespace CardNarrative.Core.Services;

public static class JsonLogicContextBuilder
{
    public static JsonLogicContext FromGameState(
        GameState state,
        Module module,
        EventOrbit? orbit = null)
    {
        var ctx = new JsonLogicContext();

        // turn
        ctx.Set("turn", state.CurrentBigRound);

        // flag.<name>
        foreach (var (key, value) in state.Flags)
        {
            ctx.Set($"flag.{key}", value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.True => System.Text.Json.Nodes.JsonValue.Create(true),
                System.Text.Json.JsonValueKind.False => System.Text.Json.Nodes.JsonValue.Create(false),
                System.Text.Json.JsonValueKind.Number => value.TryGetInt64(out var l)
                    ? System.Text.Json.Nodes.JsonValue.Create(l)
                    : System.Text.Json.Nodes.JsonValue.Create(value.GetDouble()),
                System.Text.Json.JsonValueKind.String => System.Text.Json.Nodes.JsonValue.Create(value.GetString()),
                _ => System.Text.Json.Nodes.JsonNode.Parse(value.GetRawText()),
            });
        }

        // worldFlags.<name> — 規格 §5.3 預留：暫時沒有獨立來源，與 flag 共用
        // （未來若 GameState 加 WorldFlags 字典，這裡 mirror 過去即可）

        // hero.* — 以 CurrentPlayer 為基準
        if (state.Players.Count > 0)
        {
            var player = state.CurrentPlayer;
            ctx.Set("hero.hp", player.Hp);
            ctx.Set("hero.ap", player.ActionPoints);
            if (module.Characters.TryGetValue(player.CharacterId, out var character))
            {
                ctx.Set("hero.attr.power", character.Stats.Power);
                ctx.Set("hero.attr.social", character.Stats.Social);
                ctx.Set("hero.attr.skill", character.Stats.Skill);
                ctx.Set("hero.attr.intellect", character.Stats.Intellect);
            }
        }

        // companion[i].* — 展平為 companion.0.hp / companion.0.id 等
        ctx.Set("companion.count", state.Companions.Count);
        for (int i = 0; i < state.Companions.Count; i++)
        {
            var c = state.Companions[i];
            ctx.Set($"companion.{i}.id", c.CompanionId);
            ctx.Set($"companion.{i}.hp", c.Hp);
        }

        // currentTile.*
        if (state.Players.Count > 0)
        {
            var pos = state.CurrentPlayer.Position;
            ctx.Set("currentTile.row", pos.Y);
            ctx.Set("currentTile.col", pos.X);
            if (state.TileMap.TryGetValue((pos.X, pos.Y), out var placed))
            {
                ctx.Set("currentTile.tileCardId", placed.TileId);
                if (module.Tiles.TryGetValue(placed.TileId, out var tile))
                {
                    ctx.Set("currentTile.terrain", tile.Terrain.ToString());
                }
            }
        }

        // orbit.{A,B,C}.count + orbit.contains.<eventId>
        if (orbit is not null)
        {
            int a = 0, b = 0, c = 0;
            foreach (var inst in orbit.Pending)
            {
                switch (inst.Class)
                {
                    case EventOrbitClass.ClassA: a++; break;
                    case EventOrbitClass.ClassB: b++; break;
                    case EventOrbitClass.ClassC: c++; break;
                }
                ctx.Set($"orbit.contains.{inst.Id}", true);
            }
            ctx.Set("orbit.A.count", a);
            ctx.Set("orbit.B.count", b);
            ctx.Set("orbit.C.count", c);
        }

        return ctx;
    }
}
