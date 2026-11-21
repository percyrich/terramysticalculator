using System.Text.Json.Nodes;
using Core;

namespace BgaImport;

// BGA's faction material (gamedatas.factions[type]) is the version of each faction that table was played with: it
// changes over time (Chash Dallah, Enlightened and Goblins changed between tables 491140027 and 601872433). Each
// faction in the game is compared with Core's definition, value by value, and every difference is listed in the report.
// Keys absent from the material count as 0 for incomes and starting values; absent costs are not compared.
public static class MaterialCheck
{
    public static JsonObject? MaterialOf(JsonObject gameData, Faction faction)
    {
        if (gameData["factions"] is not JsonObject all)
            return null;
        foreach (var (type, node) in all)
        {
            Faction? f = null;
            try { f = Tables.ToFaction(type); } catch { } // a BGA faction Core doesn't have
            if (f == faction && node is JsonObject material)
                return material;
        }
        return null;
    }

    public static List<string> Differences(JsonObject material, Faction faction)
    {
        var d = Factions.All[faction];
        var list = new List<string>();
        int Z(string key) => J.Int(material[key]) ?? 0;
        void Same(string what, int bga, int core)
        {
            if (bga != core)
                list.Add($"{what}: BGA {bga}, Core {core}");
        }
        void SameIfGiven(string what, string key, int core)
        {
            if (J.Int(material[key]) is int bga)
                Same(what, bga, core);
        }

        // Starting values.
        SameIfGiven("starting coins", "coins_setup", d.StartingCoins);
        SameIfGiven("starting workers", "workers_setup", d.StartingWorkers);
        Same("starting priests", Z("priests_setup"), d.StartingPriests);
        var power = $"{Z("power_bowl1_setup")}/{Z("power_bowl2_setup")}/{Z("power_bowl3_setup")}";
        var corePower = $"{d.StartingPower.Bowl1}/{d.StartingPower.Bowl2}/{d.StartingPower.Bowl3}";
        if (power != corePower)
            list.Add($"starting power: BGA {power}, Core {corePower}");
        if (faction == Faction.Goblins) // older tables: 2 starting treasures (464661609, 372743723)
            SameIfGiven("starting treasures", "token_setup", Config.GoblinsStartingTreasures);
        foreach (var (track, key) in new[] { (Track.Fire, "firecult"), (Track.Water, "watercult"), (Track.Earth, "earthcult"), (Track.Air, "aircult") })
            Same($"starting {track} steps", Z($"{key}_setup"), d.StartingSteps.IsDefaultOrEmpty ? 0 : d.StartingSteps[(int)track]);

        // Building costs.
        foreach (var (name, key, cost) in new[] { ("dwelling", "dwelling", d.DwellingCost), ("trading post", "tradinghouse", d.TradingPostCost),
                     ("temple", "temple", d.TempleCost), ("sanctuary", "sanctuary", d.SanctuaryCost), ("stronghold", "stronghold", d.StrongholdCost) })
        {
            if (faction == Faction.Atlanteans && key == "stronghold")
                continue; // their stronghold is their free starting piece (BGA: 0)
            // BGA's trading post cost is the lonely one, with tradinghouse_coins_reduced_cost next to an opponent: Core's.
            var coinKey = material.ContainsKey($"{key}_coins_reduced_cost") ? $"{key}_coins_reduced_cost" : $"{key}_coins_cost";
            // The Children of the Wyrm: BGA gives every cost lonely (doubled); Core the cost next to an opponent.
            if (faction == Faction.ChildrenOfTheWyrm && J.Int(material[$"{key}_coins_cost"]) is int lonely)
                Same($"{name} coins (lonely, halved)", lonely / 2, cost.Coins);
            else
                SameIfGiven($"{name} coins", coinKey, cost.Coins);
            SameIfGiven($"{name} workers", $"{key}_workers_cost", cost.Workers);
        }

        // Spade and shipping upgrades (when the faction has them).
        if (material["exchangetrack_upgrade"]?.ToString() != "false" && d.MaxSpadeUpgrades > 0)
        {
            SameIfGiven("spade upgrade coins", "exchangetrack_coins_cost", d.SpadeUpgradeCost.Coins);
            SameIfGiven("spade upgrade workers", "exchangetrack_workers_cost", d.SpadeUpgradeCost.Workers);
            SameIfGiven("spade upgrade VP", "exchangetrack_2_vp_gain", d.SpadeUpgradeVictoryPoints);
        }
        if (d.HasShipping && d.MaxShipping > d.StartingShipping)
            SameIfGiven("shipping upgrade coins", "shippingtrack_coins_cost", d.ShippingUpgradeCost.Coins);

        // Chash Dallah's income track: BGA's additionaltrack_k is Core's step k + 1 (what that step adds, its VP).
        if (faction == Faction.ChashDallah && material["additionaltrack"] is not null)
            for (var k = 0; k < Config.ChashDallahIncomeTrackTop; k++)
            {
                Income above = Factions.IncomeTrackIncome(k + 1), below = Factions.IncomeTrackIncome(k);
                SameIfGiven($"income track step {k + 1} coins", $"additionaltrack_{k}_coins_gain", above.Coins - below.Coins);
                SameIfGiven($"income track step {k + 1} workers", $"additionaltrack_{k}_workers_gain", above.Workers - below.Workers);
                if (k > 0)
                    SameIfGiven($"income track step {k + 1} VP", $"additionaltrack_{k}_vp_gain", Factions.IncomeTrackVictoryPoints(k + 1));
            }

        // Incomes: BGA's "dwelling_0_built" is the base income (before any dwelling is built), "_k_built" the k-th slot.
        void SameIncome(string what, string prefix, Income core)
        {
            Same($"{what} coins", Z($"{prefix}_coins_income"), core.Coins);
            Same($"{what} workers", Z($"{prefix}_workers_income"), core.Workers);
            Same($"{what} priests", Z($"{prefix}_priests_income"), core.Priests);
            Same($"{what} power", Z($"{prefix}_power_income"), core.Power);
        }
        Income At(System.Collections.Immutable.ImmutableArray<Income> incomes, int i) =>
            !incomes.IsDefaultOrEmpty && i < incomes.Length ? incomes[i] : default;
        SameIncome("base income", "dwelling_0_built", d.BaseIncome);
        for (var k = 1; k <= 8; k++)
            SameIncome($"dwelling {k} income", $"dwelling_{k}_built", At(d.DwellingIncome, k - 1));
        for (var k = 1; k <= 4; k++)
            SameIncome($"trading post {k} income", $"tradinghouse_{k}_built", At(d.TradingPostIncome, k - 1));
        for (var k = 1; k <= 3; k++)
            SameIncome($"temple {k} income", $"temple_{k}_built", At(d.TempleIncome, k - 1));
        SameIncome("sanctuary income", "sanctuary_1_built", d.SanctuaryIncome);
        SameIncome("stronghold income", "stronghold_1_built", d.StrongholdIncome);
        return list;
    }
}
