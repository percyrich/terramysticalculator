using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of Main: upgrading a building. Clicking one of your own buildings on the map shows, over it, a copy of each
// building it could be upgraded to right now (a trading post's temple and stronghold side by side), in your colour,
// nothing else. Clicking one upgrades to it; any other click (or Esc) closes them. If none is possible, the reason is shown instead. A file of the same class (partial), only to keep
// Main.cs a manageable size.
public partial class Main
{

    private Node2D _upgradeChoice;                                    // the choices, while shown (null otherwise)
    private readonly List<(Sprite2D Sprite, Move Move)> _upgradeOptions = new();
    private Sprite2D _lavaOption;           // Setup: the Kingdom of Ember's lava tile, on the left
    private Move _lavaMove;
    private System.Action _reopenChoices;   // after the lava: the choices again

    private void OfferUpgrades(GameState state, Faction active, int hex, PieceKind kind)
    {
        List<PieceKind> upgrades = Buildings.UpgradesOf(kind).ToList();
        if (upgrades.Count == 0)
        {
            ShowNote($"A {Buildings.Noun(kind)} cannot be upgraded.");
            return;
        }

        // Every upgrade the rules would accept now, and the first reason one wouldn't (shown if none would).
        var possible = new List<(PieceKind Kind, Move Move)>();
        IReadOnlyList<Issue> firstRefusal = null;
        foreach (PieceKind upgrade in upgrades)
        {
            if (state[active].NextBuilding(upgrade) is not Piece building)
                continue;
            Move move = new UpgradeBuilding(building, hex);
            MoveResult trial = Rules.Apply(state, move);
            if (trial.Accepted)
                possible.Add((upgrade, move));
            else
                firstRefusal ??= trial.Issues;
        }
        if (possible.Count == 0)
        {
            if (firstRefusal is not null)
                ShowIssues(firstRefusal); // e.g. "already taken their action"
            else
                ShowNote($"The {NameOf(active)} have nothing left to upgrade this {Buildings.Noun(kind)} to.");
            return;
        }

        ShowBuildingChoices(hex, state[active].Colour, possible);
    }

    // The buildings shown over a hex, one per choice, in the owner's colour; clicking one makes its move (also Setup's
    // replacement choice: MainModes.cs).
    private void ShowBuildingChoices(int hex, PlayerColour colour, List<(PieceKind Kind, Move Move)> possible,
        Move lava = null, System.Action reopen = null)
    {
        CloseUpgradeChoice();
        ShowNote("");
        int shown = possible.Count + (lava is null ? 0 : 1), first = lava is null ? 0 : 1;
        _upgradeChoice = new Node2D
        {
            Name = "UpgradeChoice", ZIndex = ColourWheelZIndex,
            Position = _pieceLayer.ToLocal(_map.Hex(hex).GlobalPosition) + UpgradeChoiceOffset,
        };
        _pieceLayer.AddChild(_upgradeChoice);
        for (var i = 0; i < possible.Count; i++)
        {
            var (upgrade, move) = possible[i];
            var sprite = new Sprite2D
            {
                Texture = Art.PieceTexture(upgrade, colour),
                Position = new Vector2((first + i - (shown - 1) / 2f) * UpgradeChoiceSpacing, 0),
                Scale = Vector2.One * UpgradeChoiceScale,
            };
            _upgradeChoice.AddChild(sprite);
            _upgradeOptions.Add((sprite, move));
        }
        if (lava is not null)
        {
            _lavaOption = new Sprite2D
            {
                Texture = IconArt.Texture(Icon.LavaToken, Mathf.RoundToInt(UpgradeChoiceSpacing * 0.7f)),
                Position = new Vector2(-(shown - 1) / 2f * UpgradeChoiceSpacing, 0),
            };
            _upgradeChoice.AddChild(_lavaOption);
            _lavaMove = lava;
            _reopenChoices = reopen;
        }
    }

    // A press while the choices show: on one, that upgrade (the replaced building slides back to its bank); anywhere
    // else, they close (and the press does nothing more, as with the colour wheel; buttons still work).
    private void OnPressUpgradeChoice(Vector2 mouse)
    {
        if (_lavaOption is not null && IsUnder(_lavaOption, mouse))
        {
            Move lava = _lavaMove;
            System.Action reopen = _reopenChoices;
            GetViewport().SetInputAsHandled();
            CloseUpgradeChoice();
            Submit(lava);
            reopen?.Invoke(); // open again, for more
            return;
        }
        Move chosen = _upgradeOptions.Where(option => IsUnder(option.Sprite, mouse)).Select(option => option.Move).FirstOrDefault();
        CloseUpgradeChoice();
        if (chosen is not null)
        {
            GetViewport().SetInputAsHandled();
            Submit(chosen);
        }
    }

    private void CloseUpgradeChoice()
    {
        _upgradeOptions.Clear();
        _lavaOption = null;
        _lavaMove = null;
        _reopenChoices = null;
        if (_upgradeChoice is null)
            return;
        _upgradeChoice.QueueFree();
        _upgradeChoice = null;
    }

    // ---------- Shipping and spade upgrades: click the icon on the panel ----------

    private Move UpgradeMoveAt(Vector2 point)
    {
        foreach (PlayerPanel panel in _panels)
        {
            if (panel.ShownFaction is not Faction faction)
                continue;
            if (panel.IsOverShippingIcon(point))
                return new UpgradeShipping(faction);
            if (panel.IsOverSpadeIcon(point))
                return new UpgradeSpades(faction);
        }
        return null;
    }

    // Level beside each icon, cost underneath, and whether the upgrade is possible right now.
    private static void ShowUpgrades(PlayerPanel panel, GameState state, PlayerState player)
    {
        Faction faction = player.Faction;
        int shipping = state.ShippingOf(faction); // with this round's bonus, if any
        Amounts priest = Amounts.Of(ResourceKind.Priest, 1);
        // The Alchemists' Gloomy Workshop offers them without the priest.
        Amounts shippingPriest = player.LandscapeOffers.HasFlag(LandscapeOffer.Shipping) ? Amounts.None : priest;
        Amounts spadePriest = player.LandscapeOffers.HasFlag(LandscapeOffer.Spades) ? Amounts.None : priest;
        Amounts shippingCost = player.Shipping >= Factions.MaxShipping(faction)
            ? null
            : shippingPriest + Amounts.From(Factions.ShippingUpgradeCost(faction));
        Amounts spadeCost = player.SpadeUpgrades >= Factions.MaxSpadeUpgrades(faction)
            ? null
            : spadePriest + Amounts.From(Factions.SpadeUpgradeCost(faction));

        // Where shipping goes: the Fakirs' carpet range (no upgrade cost: they never upgrade it), the Dwarves' tunnel
        // (no number: it always skips 1; what a tunnel costs beside it), or the shipping level ("2+1" with a bonus).
        Amounts level = Amounts.Of(ResourceKind.Shipping, player.Shipping);
        string levelText = shipping > player.Shipping ? $"{player.Shipping}+{shipping - player.Shipping}" : null;
        Amounts sideCost = null;
        if (faction == Faction.Fakirs)
        {
            level = Amounts.Of(ResourceKind.CarpetRange, state.CarpetRange(faction));
            levelText = null;
        }
        else if (faction == Faction.Dwarves)
        {
            level = Amounts.Of(ResourceKind.Tunnel, 1);
            levelText = "";
            sideCost = Amounts.From(Rules.ReachCost(state, faction, Reach.Tunnel));
        }
        // Fan factions: the Enlightened pay power for a spade; the Prospectors buy golden spades with coins, each
        // bringing 1 power and 1 VP (written where an upgrade's cost goes; ShowGoldenSpade).
        Amounts spadeRate = faction == Faction.Enlightened
            ? Amounts.Of(ResourceKind.PowerSpend, player.SpadePower)
            : Amounts.From(player.SpadeCost);
        if (faction == Faction.SnowShamans) // their steps are free (after passing): no costs
            shippingCost = spadeCost = null;
        if (faction == Faction.Prospectors)
            spadeCost = Amounts.Of((ResourceKind.Power, Config.ProspectorsPowerPerSpade),
                (ResourceKind.VictoryPoint, Config.ProspectorsVictoryPointsPerSpade));
        panel.ShowUpgrades(level, levelText, shippingCost, Rules.Apply(state, new UpgradeShipping(faction)).Accepted,
            sideCost, spadeRate, spadeCost, Rules.Apply(state, new UpgradeSpades(faction)).Accepted);
        panel.ShowGoldenSpade(faction == Faction.Prospectors ? spadeRate : null); // after: it hides the spade and its rate
    }
}
