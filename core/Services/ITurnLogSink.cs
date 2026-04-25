// ITurnLogSink — 回合紀錄接收介面；TurnLoop / BattleEngine / EventResolver 在關鍵結算點呼叫。
// 以介面而非具體型別傳遞，方便測試以空實作或樁代替，並避免對 UI 層的耦合。
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Services;

public interface ITurnLogSink
{
    void Append(string text, TurnLogKind kind = TurnLogKind.Neutral);
}
