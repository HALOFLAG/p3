// ModuleLoader — 從資料夾讀取並驗證模組 JSON → 產出 Module 物件。
// 先以 JSON Schema（schemas/*.json）驗證、再 deserialize 成 record。
// 錯誤以 ValidationError 彙總回傳；schema 快取以路徑為 key。
using System.Text.Json;
using System.Text.Json.Nodes;
using CardNarrative.Core.Models;
using Json.Schema;

namespace CardNarrative.Core.Services;

public sealed class ModuleLoader
{
    private readonly string _schemasFolder;
    private readonly JsonSerializerOptions _jsonOptions;
    private static readonly Dictionary<string, JsonSchema> _schemaCache = new();
    private static readonly object _schemaCacheLock = new();

    public ModuleLoader(string schemasFolder)
    {
        _schemasFolder = schemasFolder;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    public ModuleLoadResult Load(string moduleFolder)
    {
        var errors = new List<ValidationError>();

        var manifest = LoadObject<Manifest>(moduleFolder, "manifest.json", "manifest.schema.json", errors);
        if (manifest is null)
            return new ModuleLoadResult.Failure(errors);

        var prologue = LoadObject<Prologue>(moduleFolder, "prologue.json", "prologue.schema.json", errors);
        var characters  = LoadArray<Character>(moduleFolder, "characters.json",      "character.schema.json",     errors);
        var companions  = LoadArray<NpcCompanion>(moduleFolder, "npc_companions.json", "npc_companion.schema.json", errors);
        var tiles       = LoadArray<Tile>(moduleFolder, "tiles.json",                "tile.schema.json",           errors);
        var events      = LoadArray<EventCard>(moduleFolder, "events.json",          "event.schema.json",          errors);
        var actionCards = LoadArray<ActionCard>(moduleFolder, "action_cards.json",   "action_card.schema.json",    errors);
        var equipment   = LoadArray<Equipment>(moduleFolder, "equipment.json",       "equipment.schema.json",      errors);
        var endings     = LoadArray<Ending>(moduleFolder, "endings.json",            "ending.schema.json",         errors);
        var battles     = LoadArrayOptional<BattleCard>(moduleFolder, "battles.json", "battle.schema.json",       errors);

        if (prologue is null || tiles is null || characters is null || companions is null
            || events is null || actionCards is null || equipment is null || endings is null)
        {
            return new ModuleLoadResult.Failure(errors);
        }

        var tileMap = BuildIdMap(tiles, "tiles.json", t => t.Id, errors);

        if (!tileMap.ContainsKey(prologue.StartingTileId))
        {
            errors.Add(new ValidationError("prologue.json", "/startingTileId",
                $"startingTileId '{prologue.StartingTileId}' not found in tiles"));
        }

        foreach (var important in prologue.ImportantTileIds)
        {
            if (!tileMap.ContainsKey(important))
            {
                errors.Add(new ValidationError("prologue.json", "/importantTileIds",
                    $"importantTileId '{important}' not found in tiles"));
            }
        }

        // v1.12 Stage 3 — 驗證 prologue.tileBatches 內每個 tile id 存在於 tiles。
        // 每組 1-3 張的 size 限制由 schema 層擋住（minItems/maxItems），此處只查交叉引用。
        for (int bi = 0; bi < prologue.TileBatches.Count; bi++)
        {
            var batch = prologue.TileBatches[bi];
            for (int ti = 0; ti < batch.Count; ti++)
            {
                if (!tileMap.ContainsKey(batch[ti]))
                {
                    errors.Add(new ValidationError("prologue.json",
                        $"/tileBatches/{bi}/{ti}",
                        $"tileBatches tile id '{batch[ti]}' not found in tiles"));
                }
            }
        }

        foreach (var ev in events)
        {
            if (ev.Trigger is TileEnterTrigger te && !tileMap.ContainsKey(te.TileId))
            {
                errors.Add(new ValidationError("events.json", $"/[id={ev.Id}]/trigger/tileId",
                    $"tileEnter tileId '{te.TileId}' not found in tiles"));
            }
        }

        var battleMap = battles is null
            ? (IReadOnlyDictionary<string, BattleCard>)new Dictionary<string, BattleCard>(StringComparer.Ordinal)
            : BuildIdMap(battles, "battles.json", b => b.Id, errors);

        foreach (var ev in events)
        {
            ValidateEventEffects(ev, battleMap, errors);
        }

        if (errors.Count > 0)
            return new ModuleLoadResult.Failure(errors);

        var module = new Module(
            manifest,
            prologue,
            BuildIdMap(characters,  "characters.json",     c => c.Id, errors),
            BuildIdMap(companions,  "npc_companions.json", c => c.Id, errors),
            tileMap,
            BuildIdMap(events,      "events.json",         e => e.Id, errors),
            BuildIdMap(actionCards, "action_cards.json",   a => a.Id, errors),
            BuildIdMap(equipment,   "equipment.json",      e => e.Id, errors),
            BuildIdMap(endings,     "endings.json",        e => e.Id, errors),
            battleMap
        );

        return errors.Count > 0
            ? new ModuleLoadResult.Failure(errors)
            : new ModuleLoadResult.Success(module);
    }

    private T? LoadObject<T>(string moduleFolder, string fileName, string schemaName, List<ValidationError> errors)
        where T : class
    {
        var node = ReadAndValidate(moduleFolder, fileName, schemaName, errors);
        return node is null ? null : TryDeserialize<T>(node, fileName, errors);
    }

    private IReadOnlyList<T>? LoadArray<T>(string moduleFolder, string fileName, string schemaName, List<ValidationError> errors)
    {
        var node = ReadAndValidate(moduleFolder, fileName, schemaName, errors);
        return node is null ? null : TryDeserialize<List<T>>(node, fileName, errors);
    }

    private IReadOnlyList<T>? LoadArrayOptional<T>(string moduleFolder, string fileName, string schemaName, List<ValidationError> errors)
    {
        if (!File.Exists(Path.Combine(moduleFolder, fileName)))
            return null;
        return LoadArray<T>(moduleFolder, fileName, schemaName, errors);
    }

    private static void ValidateEventEffects(
        EventCard ev,
        IReadOnlyDictionary<string, BattleCard> battles,
        List<ValidationError> errors)
    {
        Walk(ev.Outcomes.Success.Effects);
        Walk(ev.Outcomes.PartialSuccess.Effects);
        Walk(ev.Outcomes.Failure.Effects);

        void Walk(IReadOnlyList<EffectBase> effects)
        {
            foreach (var e in effects)
            {
                if (e is TriggerBattleEffect tb && !battles.ContainsKey(tb.BattleId))
                {
                    errors.Add(new ValidationError("events.json",
                        $"/[id={ev.Id}]/outcomes/.../effects/triggerBattle",
                        $"battleId '{tb.BattleId}' not found in battles.json"));
                }
                if (e is RollCheckEffect rc && rc.OnFailure is { } onf)
                    Walk(onf);
            }
        }
    }

    private JsonNode? ReadAndValidate(string moduleFolder, string fileName, string schemaName, List<ValidationError> errors)
    {
        var path = Path.Combine(moduleFolder, fileName);
        if (!File.Exists(path))
        {
            errors.Add(new ValidationError(fileName, "/", $"required file '{fileName}' not found"));
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException ex)
        {
            errors.Add(new ValidationError(fileName, ex.Path ?? "/", $"invalid JSON: {ex.Message}"));
            return null;
        }

        var schema = LoadSchema(schemaName);
        var element = JsonSerializer.SerializeToElement(node);
        var results = schema.Evaluate(element, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List
        });

        if (!results.IsValid)
        {
            CollectSchemaErrors(results, fileName, errors);
            return null;
        }

        return node;
    }

    private static void CollectSchemaErrors(EvaluationResults results, string fileName, List<ValidationError> errors)
    {
        int before = errors.Count;
        CollectRecursive(results, fileName, errors);
        if (errors.Count == before)
        {
            errors.Add(new ValidationError(fileName, "/", "schema validation failed"));
        }
    }

    private static void CollectRecursive(EvaluationResults node, string fileName, List<ValidationError> errors)
    {
        if (!node.IsValid && node.Errors is { Count: > 0 })
        {
            foreach (var err in node.Errors)
            {
                var pointer = node.InstanceLocation.ToString();
                if (string.IsNullOrEmpty(pointer)) pointer = "/";
                errors.Add(new ValidationError(fileName, pointer, $"[{err.Key}] {err.Value}"));
            }
        }
        if (node.Details is { Count: > 0 })
        {
            foreach (var child in node.Details)
            {
                CollectRecursive(child, fileName, errors);
            }
        }
    }

    private T? TryDeserialize<T>(JsonNode node, string fileName, List<ValidationError> errors)
        where T : class
    {
        try
        {
            return node.Deserialize<T>(_jsonOptions);
        }
        catch (JsonException ex)
        {
            errors.Add(new ValidationError(fileName, ex.Path ?? "/", $"deserialization failed: {ex.Message}"));
            return null;
        }
    }

    private JsonSchema LoadSchema(string schemaFileName)
    {
        var key = Path.Combine(_schemasFolder, schemaFileName);
        lock (_schemaCacheLock)
        {
            if (_schemaCache.TryGetValue(key, out var cached))
                return cached;
            var schema = JsonSchema.FromFile(key);
            _schemaCache[key] = schema;
            return schema;
        }
    }

    private static IReadOnlyDictionary<string, T> BuildIdMap<T>(
        IEnumerable<T> items, string fileName, Func<T, string> idSelector, List<ValidationError> errors)
    {
        var dict = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = idSelector(item);
            if (!dict.TryAdd(id, item))
            {
                errors.Add(new ValidationError(fileName, "/", $"duplicate id '{id}'"));
            }
        }
        return dict;
    }
}
