// Phase 1 Task 3 — 角色 + HP/AP/4 屬性 + 2d6 判定 console 模擬器。
// 對應規格書 §1.2 / §1.3 與需求書 §8 Phase 1 任務 3。
//
// 用法：
//   cd p3
//   dotnet run --project tools/RollDemo
//
// 互動：
//   選角色 → 選屬性 → 輸入 TN 與地塊修正 → Enter 擲骰 → 看完整公式
//   q 離開、c 換角色

using CardNarrative.Core.Models;
using CardNarrative.Core.Services;

namespace RollDemo;

internal static class Program
{
    private const int DemoApMax = 3; // 規格書 §3.1 主角 AP 上限 = 3
    private const string Sep = "-----------------------------------------------------------";

    private static readonly DemoCharacter[] Roster =
    {
        new DemoCharacter(
            new Character(
                Id: "detective-oscar",
                Name: "奧斯卡偵探",
                Stats: new StatBlock(Power: 2, Social: 4, Skill: 3, Intellect: 3),
                HpMax: 12,
                Specialty: "社交型 — 善於對話與探查線索",
                StartingDeck: Array.Empty<string>())),
        new DemoCharacter(
            new Character(
                Id: "scholar-hilda",
                Name: "希爾達學者",
                Stats: new StatBlock(Power: 1, Social: 2, Skill: 2, Intellect: 5),
                HpMax: 8,
                Specialty: "知識型 — 解讀符文與儀式專家",
                StartingDeck: Array.Empty<string>())),
        new DemoCharacter(
            new Character(
                Id: "fighter-brawn",
                Name: "布朗格鬥家",
                Stats: new StatBlock(Power: 5, Social: 2, Skill: 3, Intellect: 1),
                HpMax: 14,
                Specialty: "戰鬥型 — 衝鋒陷陣的肉盾",
                StartingDeck: Array.Empty<string>())),
    };

    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintBanner();

        int seed = ReadSeed();
        IDiceService dice = new SeededDiceService(seed);
        Console.WriteLine($"骰子種子：{seed}（同 seed 可重現相同擲骰序列）");
        Console.WriteLine();

        DemoCharacter? current = null;

        while (true)
        {
            current ??= ChooseCharacter();
            if (current is null) return 0;

            PrintCharacterCard(current);

            var stat = ChooseStat();
            if (stat is null) { current = null; continue; }

            int tn = ReadInt("輸入目標值 TN", min: 2, max: 20, fallback: 8);
            int tileMod = ReadInt("輸入地塊修正 (可正可負)", min: -5, max: 5, fallback: 0);

            Console.Write("輸入 Enter 擲骰、q 結束、c 換角色：");
            var cmd = Console.ReadLine()?.Trim();
            if (cmd is "q" or "Q") return 0;
            if (cmd is "c" or "C") { current = null; continue; }

            DoRoll(dice, current, stat.Value, tn, tileMod);
            Console.WriteLine(Sep);
        }
    }

    private static void DoRoll(IDiceService dice, DemoCharacter character, Stat stat, int tn, int tileMod)
    {
        var roll = dice.Roll2d6();
        int statValue = StatValue(character.Character.Stats, stat);
        int total = roll.Total + statValue + tileMod;
        bool success = total >= tn;

        Console.WriteLine();
        Console.WriteLine($"擲骰結果：D1={roll.D1}, D2={roll.D2}, 2d6={roll.Total}");
        Console.WriteLine($"完整公式：2d6({roll.Total}) + {stat}({statValue}) + 地塊({FormatMod(tileMod)}) = {total} vs TN={tn}");
        Console.WriteLine($"判定結果：{(success ? "✓ 成功" : "✗ 失敗")}");

        // 規格書 §3.3 / §10.5 雙 6/雙 1 奇蹟機制（本 demo 僅顯示訊息，不改判定）
        if (roll.IsDouble6) Console.WriteLine("  ★ 雙 6：氣勢如虹！（規格書 §3.3 戰鬥內可強制成功 / 帶 Advantage）");
        if (roll.IsDouble1) Console.WriteLine("  ☠ 雙 1：趨勢崩壞！（規格書 §3.3 戰鬥內標記 vulnerable）");
    }

    private static int StatValue(StatBlock stats, Stat stat) => stat switch
    {
        Stat.Power => stats.Power,
        Stat.Social => stats.Social,
        Stat.Skill => stats.Skill,
        Stat.Intellect => stats.Intellect,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    private static string FormatMod(int n) => n >= 0 ? $"+{n}" : n.ToString();

    private static DemoCharacter? ChooseCharacter()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== 選擇角色 ===");
            for (int i = 0; i < Roster.Length; i++)
            {
                var c = Roster[i].Character;
                Console.WriteLine($"  {i + 1}. {c.Name} — {c.Specialty}");
            }
            Console.WriteLine("  q. 結束");
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();
            if (input is null) return null; // EOF (Ctrl+Z / piped end)
            if (input.Length == 0 || input is "q" or "Q") return null;
            if (int.TryParse(input, out int n) && n >= 1 && n <= Roster.Length)
            {
                return Roster[n - 1];
            }
            Console.WriteLine("無效輸入，請重試。");
        }
    }

    private static Stat? ChooseStat()
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== 選擇屬性 ===");
            Console.WriteLine("  1. Power（紅戰鬥）");
            Console.WriteLine("  2. Social（藍社交）");
            Console.WriteLine("  3. Skill（綠探索）");
            Console.WriteLine("  4. Intellect（紫知識）");
            Console.WriteLine("  c. 換角色");
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim();
            if (input is null) return null; // EOF
            if (input is "c" or "C") return null;
            switch (input)
            {
                case "1": return Stat.Power;
                case "2": return Stat.Social;
                case "3": return Stat.Skill;
                case "4": return Stat.Intellect;
                default: Console.WriteLine("無效輸入，請重試。"); break;
            }
        }
    }

    private static int ReadInt(string prompt, int min, int max, int fallback)
    {
        Console.Write($"{prompt} [{min}~{max}, 預設 {fallback}]：");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) return fallback;
        if (int.TryParse(input, out int n) && n >= min && n <= max) return n;
        Console.WriteLine($"  → 輸入無效，使用預設值 {fallback}");
        return fallback;
    }

    private static int ReadSeed()
    {
        Console.Write("輸入骰子種子（預設 1234）：");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(input)) return 1234;
        if (int.TryParse(input, out int s)) return s;
        Console.WriteLine("  → 種子無效，使用預設值 1234");
        return 1234;
    }

    private static void PrintBanner()
    {
        Console.WriteLine(Sep);
        Console.WriteLine(" 廢棄洋房調查 — Phase 1 Task 3 擲骰示範器");
        Console.WriteLine(" CardNarrative.Core 角色 / 2d6 / 屬性檢定 console demo");
        Console.WriteLine(Sep);
    }

    private static void PrintCharacterCard(DemoCharacter dc)
    {
        var c = dc.Character;
        Console.WriteLine();
        Console.WriteLine(Sep);
        Console.WriteLine($" {c.Name}    HP {c.HpMax} / AP {DemoApMax}");
        Console.WriteLine($" 專長：{c.Specialty}");
        Console.WriteLine($" 屬性：紅戰鬥 Power={c.Stats.Power}  藍社交 Social={c.Stats.Social}  綠探索 Skill={c.Stats.Skill}  紫知識 Intellect={c.Stats.Intellect}");
        Console.WriteLine(Sep);
    }
}

/// <summary>包裝 Core.Character + demo-only AP（Character record 本身無 AP 欄位）。</summary>
internal sealed record DemoCharacter(Character Character);
