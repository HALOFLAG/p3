using Godot;

namespace HauntedManor.Scripts;

/// <summary>
/// Phase 1 Task 1 — Main scene bootstrap placeholder.
/// 確認 Godot 可載入 CardNarrative.Core；後續 Task 將以此為入口接入 GameLifecycle。
/// </summary>
public partial class MainBootstrap : Control
{
    public override void _Ready()
    {
        var coreVersion = typeof(CardNarrative.Core.State.GameState).Assembly
            .GetName().Version?.ToString() ?? "unknown";
        GD.Print($"[HauntedManor] Phase 1 Task 1 ready. CardNarrative.Core v{coreVersion} loaded.");
    }
}
