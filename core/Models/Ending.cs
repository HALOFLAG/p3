// Ending — 結局定義（S/A/B/C 四級），以分數 Threshold 比對 Prologue.ScoreFormula 結果。
namespace CardNarrative.Core.Models;

public sealed record Ending(
    string Id,
    EndingGrade Grade,
    int Threshold,
    string Narrative
);
