// CombatActions — 戰鬥中玩家「行動選擇」與「反應選擇」的資料型別。
// PlayerActionChoice：基本行動 / 角色行動 / 動作卡三類，用來送入 BattleEngine.ResolvePlayerAction。
// PlayerResponseChoice：敵人攻擊時玩家的回應方式（接受 / 迴避 / 格擋 / 反擊 / 反射）。
namespace CardNarrative.Core.Models;

public enum BasicActionKind
{
    /// <summary>通用攻擊：2d6 + Power vs 敵方閃避，命中後造成基本傷害。</summary>
    Attack,
    /// <summary>防禦姿態：本回合提高閃避。</summary>
    Defend,
    /// <summary>觀察：重新執行一次遭遇檢定，升級揭露等級與先攻。</summary>
    Observe,
    /// <summary>就地重整：戰鬥內位置抽象 — 下次迴避 +2。</summary>
    Reposition,
    /// <summary>撤退：結束戰鬥（以玩家逃跑結算）並將玩家移回相鄰地塊（若地圖層可接上）。</summary>
    Retreat
}

public abstract record PlayerActionChoice;
public sealed record BasicActionChoice(BasicActionKind Kind) : PlayerActionChoice;
public sealed record CharacterActionChoice(string AbilityId) : PlayerActionChoice;
public sealed record CardActionChoice(string CardId) : PlayerActionChoice;

public enum PlayerResponseKind
{
    /// <summary>接受：不擲骰、不花費，直接結算敵人動作的全部 Payload。</summary>
    Accept,
    /// <summary>迴避：擲 2d6 + Skill，成功則無傷；消耗 1 AP。</summary>
    Dodge,
    /// <summary>格擋：擲 2d6 + Power + 盾 Defense，成功則傷害半減；消耗 1 AP。</summary>
    Block,
    /// <summary>反擊：擲 2d6 + Power，成功則無傷並對敵方造成 Power 傷害，失敗受全傷；消耗 1 AP。</summary>
    Counter,
    /// <summary>反射：需要手上一張 Tag 含 "reflect" 的動作卡，消耗該卡 + 1 AP；成功則反彈一半傷害。</summary>
    Reflect
}

public abstract record PlayerResponseChoice;
public sealed record AcceptResponse() : PlayerResponseChoice;
public sealed record DodgeResponse() : PlayerResponseChoice;
public sealed record BlockResponse() : PlayerResponseChoice;
public sealed record CounterResponse() : PlayerResponseChoice;
public sealed record ReflectResponse(string CardId) : PlayerResponseChoice;
