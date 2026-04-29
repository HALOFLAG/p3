// JsonLogicContextBuilder — 從 GameState + Module + EventOrbit 組出 JsonLogic 變數命名空間。
//
// 規格書 §5.3 / §7.3 列出的核心變數：
//   flag.<name>、hero.{hp,ap,hpMax,hpRatio,attr.*}、companion[i].{hp,id}、companion.count、
//   turn、currentTile.{terrain,row,col,tileCardId}、
//   tile.<row>.<col>.{terrain,tileCardId,isImportant,level}、
//   orbit.{A,B,C}.count、orbit.contains.<eventId>、worldFlags.<name>
//
// Phase 3 任務 14（S3）新增變數（A1–A5 條件用）：
//   - tilePlaced.<id>          (bool)   A1 by id：state.TileMap 含此 id 的 tile
//   - tilePlacedTag.<tag>      (bool)   A1 by tag：任一已放置 tile.Tags 含此 tag
//   - hero.hpMax / hero.hpRatio (int/double) A2：屬性 / 血量區間
//   - event.<id>.consumed      (bool)   A3：事件已消費過
//   - event.<id>.outcome       (string) A3："success" / "partialSuccess" / "failure"
//   - hasEquipment.<id>        (bool)   A4：玩家持有指定裝備（Equipment 槽 ∪ Backpack）
//   - hasIntel.<id>            (bool)   A5：玩家持有指定情報（state.AcquiredIntel）
//   - action.<kind>.count      (int)    PlayerAction trigger preview / cond
//
// 設計選擇：
//   - companion[i] 的索引語意以「state.Companions」順序為準（i 從 0 起）。
//     展平命名為 companion.0.hp / companion.1.hp …，符合 JsonLogic var 的 dot-segment 取值。
//   - orbit.contains.<eventId> 採布林 flag 寫法（只放在 orbit 上的 id 為 true，未列入即視為缺值 = false）。
//   - tile.<row>.<col>.* 預先展開所有已放置 tile（PlacedTile 通常 < 30 個，pre-populate 比 lazy 簡單）。
//     row = Position.Y, col = Position.X（與 currentTile 一致）；負座標以 "-1" 等字串 key 存放。
//   - 不存在的角色 / 同伴欄位完全跳過，避免「null var」誤觸發 JsonLogic 預設行為。
//   - 「布林索引」變數（hasIntel.* / hasEquipment.* / tilePlaced.* / event.*.consumed）只在條件成立時 set true；
//     不存在 = JsonLogic var 缺值 = false，與 EventOrbit.contains 同慣例。
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
            // Phase 3 任務 14（S3）· hpMax / hpRatio 給 A2「血量區間」條件使用。
            ctx.Set("hero.hpMax", player.HpMax);
            ctx.Set("hero.hpRatio", player.HpMax > 0 ? player.Hp / (double)player.HpMax : 0.0);
            if (module.Characters.TryGetValue(player.CharacterId, out var character))
            {
                ctx.Set("hero.attr.power", character.Stats.Power);
                ctx.Set("hero.attr.social", character.Stats.Social);
                ctx.Set("hero.attr.skill", character.Stats.Skill);
                ctx.Set("hero.attr.intellect", character.Stats.Intellect);
            }

            // Phase 3 任務 14（S3）· hasEquipment.<id> — 掃 Equipment 槽位 + Backpack。
            // A4「持有特定裝備」條件使用：模組 JSON 寫 {"var":"hasEquipment.holy-water-vial"}。
            foreach (var (_, equipId) in player.Equipment)
            {
                if (!string.IsNullOrEmpty(equipId))
                    ctx.Set($"hasEquipment.{equipId}", true);
            }
            foreach (var equipId in player.Backpack)
            {
                if (!string.IsNullOrEmpty(equipId))
                    ctx.Set($"hasEquipment.{equipId}", true);
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

        // tile.<row>.<col>.* — 展開所有已放置地塊（row=Y, col=X，與 currentTile 對齊）
        // Phase 3 任務 14（S3）· 同時建 tilePlaced.<id> / tilePlacedTag.<tag> 語法糖（A1 條件用）
        var placedTagSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ((x, y), placedTile) in state.TileMap)
        {
            var prefix = $"tile.{y}.{x}";
            ctx.Set($"{prefix}.tileCardId", placedTile.TileId);
            ctx.Set($"{prefix}.level", (int)placedTile.Level);
            ctx.Set($"tilePlaced.{placedTile.TileId}", true);
            if (module.Tiles.TryGetValue(placedTile.TileId, out var tileDef))
            {
                ctx.Set($"{prefix}.terrain", tileDef.Terrain.ToString());
                ctx.Set($"{prefix}.isImportant", tileDef.Important);
                foreach (var tag in tileDef.Tags)
                    placedTagSet.Add(tag);
            }
        }
        foreach (var tag in placedTagSet)
            ctx.Set($"tilePlacedTag.{tag}", true);

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

        // Phase 3 任務 14（S3）· event.<id>.consumed (bool) + event.<id>.outcome (string)
        // A3 「特定事件曾被消費」「事件結果為成功 / 失敗」條件使用。
        foreach (var consumedId in state.ConsumedEventIds)
        {
            ctx.Set($"event.{consumedId}.consumed", true);
        }
        foreach (var (eventId, tier) in state.EventOutcomes)
        {
            // 採與 EventOutcomeTier enum 對應的小寫 camelCase 字串：success / partialSuccess / failure
            ctx.Set($"event.{eventId}.outcome", tier switch
            {
                EventOutcomeTier.Success => "success",
                EventOutcomeTier.PartialSuccess => "partialSuccess",
                EventOutcomeTier.Failure => "failure",
                _ => "unknown"
            });
        }

        // Phase 3 任務 14（S3）· hasIntel.<id> (bool) — A5 持有情報條件。
        // S4 起 grantIntel effect 寫入 state.AcquiredIntel 後此處自動曝給 JsonLogic。
        foreach (var intelId in state.AcquiredIntel)
        {
            ctx.Set($"hasIntel.{intelId}", true);
        }

        // Phase 3 任務 14（S3）· action.<kind>.count (int) — playerAction trigger 的條件 / preview 用。
        // 採 enum 字串小寫（與 PlayerActionKind JSON 表示一致）：action.move.count / action.observe.count ...
        foreach (var (kind, count) in state.ActionCounts)
        {
            var lowerKind = char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString().Substring(1);
            ctx.Set($"action.{lowerKind}.count", count);
        }

        return ctx;
    }
}
