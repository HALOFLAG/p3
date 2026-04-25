// Manifest — 模組元資料（Id/Name/Version/Author/SchemaVersion/Description/...）。
// 由 ModuleLoader 從 manifest.json 讀入，供模組選擇介面顯示。
namespace CardNarrative.Core.Models;

public sealed record Manifest(
    string Id,
    string Name,
    string Version,
    string Author,
    int SchemaVersion,
    string Description,
    string EstimatedPlayTime,
    Difficulty? Difficulty = null
);

public sealed record Difficulty(int Stars, string Label = "");
