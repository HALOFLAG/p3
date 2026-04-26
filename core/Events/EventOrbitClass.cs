// EventOrbitClass — ORBIT 軌道槽位的揭露/觸發狀態（規格書 §1.6 / §3.6）。
// Hidden  : 尚未進入軌道（建構期狀態，不展示）
// ClassC  : 已進入軌道但 reveal 尚未通過（卡背朝上）
// ClassB  : 已揭露，trigger 條件未滿足（灰色）
// ClassA  : 已揭露且 trigger 滿足，待 EventCheck 結算（亮色）
// 結局卡與一般事件共用本枚舉，IsEnding 旗標獨立追蹤於 EventInstance。
namespace CardNarrative.Core.Events;

public enum EventOrbitClass
{
    Hidden = 0,
    ClassC = 1,
    ClassB = 2,
    ClassA = 3,
}
