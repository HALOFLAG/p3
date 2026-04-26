// EventOrbit — ORBIT 任務板核心狀態（規格書 §1.6 / §3.6 / §4.2 區塊 #15）。
//
// 顯示模型：
//   - 7 個槽位（VisibleSlots = 7），第 8 位置為「展開全部」按鈕
//   - 槽位排序：A 最左，其次 B，最後 C；同類別依加入順序（FIFO）
//   - 結局卡（IsEnding）視同對應 Class 排序，但 UI 可額外標記金邊
//
// 狀態管理：
//   - Pending : 所有尚未被 EventCheck 結算的事件實例（含 ClassC/B/A、結局卡）
//   - 第 8 槽以後的事件透過「展開全部」按鈕完整檢視，仍在 Pending 中
//   - DeferredToNext : 連鎖深度超限或本回合未處理完，下回合 EventCheck 開頭
//                     重新插回 Pending 最前端（規格 §1.6 連鎖深度上限 5）
//
// 同步：呼叫變更狀態的方法後會 raise OrbitChanged event；UI 層（Godot）訂閱即可。
// Core 不依賴 Godot Signal — UI 在 EventBus 中將 .NET event 轉成 Godot Signal。
using System.Collections.ObjectModel;
using CardNarrative.Core.Models;

namespace CardNarrative.Core.Events;

public sealed class EventOrbit
{
    /// <summary>規格 §1.6：UI 顯示 7 槽 + 第 8 位置展開鈕。</summary>
    public const int VisibleSlots = 7;

    /// <summary>規格 §1.6：連鎖深度上限 5；超出搬至下回合 ORBIT 最前端。</summary>
    public const int MaxChainDepth = 5;

    private readonly List<EventInstance> _pending = new();
    private readonly List<EventInstance> _deferredToNext = new();
    private readonly HashSet<string> _triggeredThisCheck = new(StringComparer.Ordinal);

    /// <summary>所有未結算的事件實例（含結局卡、各 Class）。對外只讀。</summary>
    public IReadOnlyList<EventInstance> Pending => _pending;

    /// <summary>連鎖深度超限被推遲到下回合的事件。</summary>
    public IReadOnlyList<EventInstance> DeferredToNext => _deferredToNext;

    /// <summary>本次 EventCheck 已觸發過（避免同 ID 在同一 Check 內重複觸發）。</summary>
    public IReadOnlyCollection<string> TriggeredThisCheck => _triggeredThisCheck;

    /// <summary>當前 EventCheck 的連鎖深度（每觸發 1 個 A 類 +1）。</summary>
    public int ChainDepth { get; private set; }

    /// <summary>當槽位內容變動時觸發；UI 訂閱以重繪 7 槽。</summary>
    public event Action? OrbitChanged;

    // ---- 槽位視圖 ----------------------------------------------------------

    /// <summary>排序後的可見槽位（最多 VisibleSlots 個）。A → B → C，FIFO。</summary>
    public IReadOnlyList<EventInstance> VisibleSlotsView()
    {
        return SortedAll().Take(VisibleSlots).ToList().AsReadOnly();
    }

    /// <summary>是否需要顯示第 8「展開全部」按鈕（總事件數 &gt; 7）。</summary>
    public bool HasExpandButton => _pending.Count > VisibleSlots;

    /// <summary>展開後的完整列表（A → B → C，FIFO）。</summary>
    public IReadOnlyList<EventInstance> ExpandedView()
    {
        return new ReadOnlyCollection<EventInstance>(SortedAll().ToList());
    }

    // ---- 狀態變更 ---------------------------------------------------------

    /// <summary>把事件加入軌道。預設為 ClassC（未揭露）；結局卡 IsEnding=true 仍走相同 reveal/trigger 流程。</summary>
    public void Push(EventInstance instance)
    {
        if (instance.Class == EventOrbitClass.Hidden)
            instance.Class = EventOrbitClass.ClassC;
        _pending.Add(instance);
        OrbitChanged?.Invoke();
    }

    /// <summary>移除（已結算或被消化）。</summary>
    public bool Remove(EventInstance instance)
    {
        var ok = _pending.Remove(instance);
        if (ok) OrbitChanged?.Invoke();
        return ok;
    }

    public bool RemoveById(string eventId)
    {
        var idx = _pending.FindIndex(e => e.Id == eventId);
        if (idx < 0) return false;
        _pending.RemoveAt(idx);
        OrbitChanged?.Invoke();
        return true;
    }

    /// <summary>把實例的 Class 換成新值（reveal/trigger 評估後呼叫）；變更則觸發 OrbitChanged。</summary>
    public void Promote(EventInstance instance, EventOrbitClass newClass)
    {
        if (instance.Class == newClass) return;
        instance.Class = newClass;
        OrbitChanged?.Invoke();
    }

    // ---- EventCheck 階段控制 ----------------------------------------------

    /// <summary>EventCheck 開始：把上回合 deferred 的事件插回最前端、清空本次 Check 狀態。</summary>
    public void BeginEventCheck()
    {
        if (_deferredToNext.Count > 0)
        {
            // 規格 §1.6：超鏈深度的事件「移至下回合 ORBIT 最前端」
            _pending.InsertRange(0, _deferredToNext);
            _deferredToNext.Clear();
        }
        _triggeredThisCheck.Clear();
        ChainDepth = 0;
        OrbitChanged?.Invoke();
    }

    /// <summary>記錄事件本次 Check 已觸發過；同 ID 重複 trigger 將被 IsTriggeredThisCheck 擋掉。</summary>
    public void MarkTriggered(string eventId)
    {
        _triggeredThisCheck.Add(eventId);
    }

    public bool IsTriggeredThisCheck(string eventId) => _triggeredThisCheck.Contains(eventId);

    /// <summary>連鎖深度 +1，並回報是否仍在上限內。超出時呼叫者應把後續事件 DeferToNext。</summary>
    public bool IncrementChainDepth()
    {
        ChainDepth++;
        return ChainDepth < MaxChainDepth;
    }

    /// <summary>把事件移到下回合 queue 最前端（規格 §1.6 連鎖超限）。</summary>
    public void DeferToNext(EventInstance instance)
    {
        _pending.Remove(instance);
        _deferredToNext.Add(instance);
        OrbitChanged?.Invoke();
    }

    // ---- 內部排序 ---------------------------------------------------------

    private IEnumerable<EventInstance> SortedAll()
    {
        // A → B → C；同 Class 內維持加入順序（穩定排序）
        return _pending
            .Select((inst, idx) => (inst, idx))
            .OrderBy(t => OrderForClass(t.inst.Class))
            .ThenBy(t => t.idx)
            .Select(t => t.inst);
    }

    private static int OrderForClass(EventOrbitClass c) => c switch
    {
        EventOrbitClass.ClassA => 0,
        EventOrbitClass.ClassB => 1,
        EventOrbitClass.ClassC => 2,
        _ => 3, // Hidden 不應出現於 _pending；放最後保險
    };
}
