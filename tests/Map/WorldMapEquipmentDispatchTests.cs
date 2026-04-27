// PR-A · WorldMap 裝備系統 dual-mode dispatch 測試（規格書 §3.4.3）。
// 驗證 state-mode 下：
//   - LoadEquipmentInventory 寫進 state.CurrentPlayer.Backpack（不再走 WorldMap._backpack）
//   - MoveEquipmentSlotToBackpack / MoveEquipmentBackpackToSlot 操作 state.CurrentPlayer.{Equipment, Backpack}
//   - MoveCharacterCardToSlot 寫 state.CurrentPlayer.CharacterCardSlot
//   - NotifyEquipmentChanged 觸發 EquipmentChanged event（含 batch flush 順序）
using CardNarrative.Core.Cards;
using CardNarrative.Core.Map;
using CardNarrative.Core.Models;
using CardNarrative.Core.Services;
using CardNarrative.Core.State;
using FluentAssertions;
using CardNarrative.Tests.Services;

namespace CardNarrative.Tests.Map;

public class WorldMapEquipmentDispatchTests
{
    private sealed class NoSubstituteRandom : IRandomProvider
    {
        public double NextDouble() => 0.99;
        public int Next(int maxExclusive) => 0;
    }

    private static (Module module, GameState state, WorldMap map) NewStateBackedMap()
    {
        var loader = new ModuleLoader(TestPaths.SchemasFolder);
        var module = ((ModuleLoadResult.Success)loader.Load(TestPaths.AbandonedMansionFolder)).Module;
        var heroId = module.Characters.Keys.First();
        var state = GameState.CreateNew(
            module,
            chosenCharacterIds: new[] { heroId },
            chosenCompanionIds: module.Prologue.StartingCompanionIds,
            seed: 1234,
            gridSize: 9,
            startPosition: new Position(4, 4));
        var map = new WorldMap(state, module, new NoSubstituteRandom());
        return (module, state, map);
    }

    [Fact]
    public void StateMode_LoadEquipmentInventory_WritesGameStateBackpack()
    {
        // PR-A · backpack 不再是 WorldMap 內部欄位；應寫進 state.CurrentPlayer.Backpack
        var (module, state, map) = NewStateBackedMap();
        var firstThree = module.Equipment.Keys.Take(3).ToList();

        map.LoadEquipmentInventory(module.Equipment, firstThree);

        state.CurrentPlayer.Backpack.Should().BeEquivalentTo(firstThree, opts => opts.WithStrictOrdering());
        map.Backpack.Should().BeEquivalentTo(firstThree, opts => opts.WithStrictOrdering());
    }

    [Fact]
    public void StateMode_LoadEquipmentInventory_HonorsBackpackMax()
    {
        var (module, state, map) = NewStateBackedMap();
        var allEquipment = module.Equipment.Keys.Take(10).ToList(); // 超過 BackpackMax (= 3)

        map.LoadEquipmentInventory(module.Equipment, allEquipment);

        state.CurrentPlayer.Backpack.Count.Should().Be(EquipmentManager.BackpackMax);
    }

    [Fact]
    public void StateMode_MoveEquipmentBackpackToSlot_MutatesGameState()
    {
        // 建一件可放 Weapon 槽的裝備並注入背包，再移到主槽，驗證 state.CurrentPlayer.Equipment 寫入。
        var (module, state, map) = NewStateBackedMap();
        // 從 module 找一件 Weapon 槽裝備
        var weaponEntry = module.Equipment.First(kv => kv.Value.Slot == EquipmentSlot.Weapon);
        var weaponId = weaponEntry.Key;
        map.LoadEquipmentInventory(module.Equipment, new[] { weaponId });
        // 確認角色卡不擋路
        if (state.CurrentPlayer.CharacterCardSlot == EquipmentSlot.Weapon)
            state.CurrentPlayer.CharacterCardSlot = EquipmentSlot.Head;

        var result = map.MoveEquipmentBackpackToSlot(0, EquipmentSlot.Weapon);

        result.Should().Be(MoveEquipmentResult.Ok);
        state.CurrentPlayer.Equipment[EquipmentSlot.Weapon].Should().Be(weaponId);
        state.CurrentPlayer.Backpack.Should().BeEmpty();
    }

    [Fact]
    public void StateMode_MoveEquipmentSlotToBackpack_MutatesGameState()
    {
        var (module, state, map) = NewStateBackedMap();
        // 預先在 Weapon 槽放一件
        var weaponEntry = module.Equipment.First(kv => kv.Value.Slot == EquipmentSlot.Weapon);
        var weaponId = weaponEntry.Key;
        map.LoadEquipmentInventory(module.Equipment, System.Array.Empty<string>());
        if (state.CurrentPlayer.CharacterCardSlot == EquipmentSlot.Weapon)
            state.CurrentPlayer.CharacterCardSlot = EquipmentSlot.Head;
        state.CurrentPlayer.Equipment[EquipmentSlot.Weapon] = weaponId;

        var result = map.MoveEquipmentSlotToBackpack(EquipmentSlot.Weapon);

        result.Should().Be(MoveEquipmentResult.Ok);
        state.CurrentPlayer.Equipment[EquipmentSlot.Weapon].Should().BeNull();
        state.CurrentPlayer.Backpack.Should().ContainSingle().Which.Should().Be(weaponId);
    }

    [Fact]
    public void StateMode_CharacterCardSlot_DispatchesToGameState()
    {
        // PR-A · CharacterCardSlot 改 dual-mode dispatch 至 state.CurrentPlayer.CharacterCardSlot
        var (module, state, map) = NewStateBackedMap();
        state.CurrentPlayer.CharacterCardSlot = EquipmentSlot.Head;
        map.CharacterCardSlot.Should().Be(EquipmentSlot.Head);

        // MoveCharacterCardToSlot：應寫進 state，而非 WorldMap 內部欄位
        var result = map.MoveCharacterCardToSlot(EquipmentSlot.Body);

        result.Should().Be(MoveEquipmentResult.Ok);
        state.CurrentPlayer.CharacterCardSlot.Should().Be(EquipmentSlot.Body);
        map.CharacterCardSlot.Should().Be(EquipmentSlot.Body);
    }

    [Fact]
    public void NotifyEquipmentChanged_FiresEquipmentChangedEvent()
    {
        // PR-A · MainBootstrap 在 GrantEquipmentEffect 套用後呼叫 NotifyEquipmentChanged，
        // UI 訂閱者（LeftPanel）應收到 EquipmentChanged event 並重繪。
        var (_, _, map) = NewStateBackedMap();
        int fired = 0;
        map.EquipmentChanged += () => fired++;

        map.NotifyEquipmentChanged();

        fired.Should().Be(1);
    }

    [Fact]
    public void NotifyEquipmentChanged_InsideBatch_FiresOnlyAfterEndBatch()
    {
        // PR-A · NotifyEquipmentChanged 走 RaiseOrQueue → batch 進行中時延後 emit。
        var (_, _, map) = NewStateBackedMap();
        int fired = 0;
        map.EquipmentChanged += () => fired++;

        map.BeginEventBatch();
        map.NotifyEquipmentChanged();
        fired.Should().Be(0); // 還沒 flush

        map.EndEventBatch();
        fired.Should().Be(1); // flush 後 emit
    }

    [Fact]
    public void NotifyEquipmentChanged_BatchOrder_FiresBetweenTileChangedAndTileTransformed()
    {
        // PR-A · EventPriority.EquipmentChanged = 55，介於 ModeChanged(50) 與 CompanionChanged(60) 之間，
        // 早於 TileTransformed(70)、晚於 TileChanged(10)。確保 UI 收到順序是「紋理 → 裝備 → 動畫」。
        var (_, _, map) = NewStateBackedMap();
        var order = new List<string>();
        map.TileChanged += (_, _) => order.Add("tile");
        map.EquipmentChanged += () => order.Add("equip");
        map.TileTransformed += (_, _, _, _) => order.Add("transform");

        map.BeginEventBatch();
        // 故意亂序丟進 buffer
        map.NotifyTileTransformed(0, 0, MapTerrain.Building, MapTerrain.Forest);
        map.NotifyEquipmentChanged();
        map.NotifyTileChanged(0, 0);
        map.EndEventBatch();

        order.Should().Equal("tile", "equip", "transform");
    }
}
