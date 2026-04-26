using Godot;

namespace HauntedManor.Scripts;

/// <summary>
/// Phase 2 Task 9 — Core .NET event ↔ Godot Signal 中介（規格書 §4.2 共用基底）。
///
/// 設計目的：
///   - UI 區塊（OrbitPanel 等）訂閱 EventBus，而非直接訂閱 core/ service，降低耦合
///   - 玩 Godot Signal 模式：UI 可在 Editor 中連線、可被 Tween / Animation 等系統呼叫
///   - 單例 — 由 MainBootstrap._Ready 建立並掛在主場景之下；其他 Node 透過 EventBus.Instance 取用
///
/// 目前只有 ORBIT 一條 Signal；隨任務增加陸續補（hand_changed / equipment_changed / ...）
/// </summary>
public partial class EventBus : Node
{
    public static EventBus? Instance { get; private set; }

    /// <summary>ORBIT 軌道內容變動（push / remove / promote / EventCheck 結束等）。</summary>
    [Signal]
    public delegate void OrbitChangedEventHandler();

    public override void _Ready()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>由 Core EventOrbit.OrbitChanged 委派呼叫；可在任何執行緒安全呼叫（內部使用 CallDeferred）。</summary>
    public void EmitOrbitChanged()
    {
        // Core 可能在非主執行緒被觸發（雖然目前都同步），用 CallDeferred 確保 main thread emit
        CallDeferred(MethodName.RaiseOrbitChanged);
    }

    private void RaiseOrbitChanged()
    {
        EmitSignal(SignalName.OrbitChanged);
    }
}
