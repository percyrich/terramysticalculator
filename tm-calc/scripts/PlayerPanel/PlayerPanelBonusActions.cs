using System.Collections.Generic;
using System.Linq;
using Core;
using Godot;

namespace TMCalc;

// The rest of PlayerPanel: bonus actions, the book, and the Treasurers' Safe. A file of the same class (partial).
public partial class PlayerPanel
{
    // ---------- Bonus actions ----------

    // An octagon for every unlocked bonus action, laid out by BonusActionStep (and the row settings), in the order
    // they were gained: the first gained is first, each new one goes after the others, and when one is lost (a pass
    // tile handed back, a move undone) the ones after it slide up to fill the gap. An X over each one used this round.
    // The order is remembered here, as the game state doesn't keep it; a panel showing another faction starts
    // afresh, in the order PlayerState.BonusActions lists them. selected: the one waiting for a target, highlighted.
    // book: the faction's book (Art.Book), always first and never crossed out; null for none.
    public void ShowBonusActions(ValueList<BonusActionId> unlocked, ValueList<BonusActionId> used, BonusActionId? selected,
        BookView book = null)
    {
        ShowBookAction(book);
        bool shownBook = _bookAction is not null && !IsCompact;
        if (_bonusFaction != ShownFaction)
        {
            foreach (ActionToken old in _octagons.Values)
                old.QueueFree();
            _octagons.Clear();
            _bonusOrder.Clear();
            _bonusFaction = ShownFaction;
        }
        foreach (BonusActionId gone in _octagons.Keys.Where(bonus => !unlocked.Contains(bonus)).ToList())
        {
            _octagons[gone].QueueFree();
            _octagons.Remove(gone);
        }
        _bonusOrder.RemoveAll(bonus => !unlocked.Contains(bonus));
        _bonusOrder.AddRange(unlocked.Where(bonus => !_bonusOrder.Contains(bonus)));

        foreach (BonusActionId bonus in _bonusOrder)
        {
            if (!_octagons.ContainsKey(bonus))
                CreateOctagon(bonus).Position = BonusPlace(_bonusOrder.IndexOf(bonus), shownBook);
            ActionToken octagon = _octagons[bonus];
            octagon.Modulate = bonus == selected ? SelectedTint : Colors.White;
            octagon.IsUsed = used.Contains(bonus);
        }
        PlaceBonusActions();
    }

    // The book action (taken any number of times: the Engineers' bridge) only in the full layout; the octagons after it
    // in order, sliding along when one comes or goes.
    private void PlaceBonusActions()
    {
        bool book = _bookAction is not null && !IsCompact;
        if (_bookAction is not null)
            _bookAction.Visible = book;
        for (var place = 0; place < _bonusOrder.Count; place++)
        {
            if (!_octagons.TryGetValue(_bonusOrder[place], out ActionToken octagon))
                continue;
            Vector2 target = BonusPlace(place, book);
            if (octagon.Position == target)
                continue;
            if (BonusActionSlideSeconds > 0 && IsInsideTree())
                CreateTween().TweenProperty(octagon, "position", target, BonusActionSlideSeconds)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            else
                octagon.Position = target;
        }
    }

    // The octagon's picture: an amount in its Content bundle (two spades, two cult steps; set out as on the power
    // actions), or its icon.
    private ActionToken CreateOctagon(BonusActionId bonus)
    {
        ActionToken octagon = ActionToken.Create();
        _bonusActionRow.AddChild(octagon);
        if (Art.BonusActionContent(bonus.Action) is Amounts content && octagon.Content is ResourceBundle bundle)
        {
            bundle.IconSize = BonusContentIconSize;
            bundle.RepeatIconsExcept = "spades";
            bundle.RepeatSpacing = BonusContentRepeatSpacing;
            bundle.ShowOnes = true;
            bundle.ShowAmounts(content);
        }
        else
            octagon.ShowIcon(Art.BonusActionIcon(bonus.Action, ShownFaction)); // in the faction's colour, where it has pieces
        _octagons[bonus] = octagon;
        return octagon;
    }

    // The book action: a book behind the octagon's place (Art.BookActionBacking), with its icon on the panel; or
    // Chash Dallah's wide book, or the Treasurers' Safe (BookView). Those are wider than a book: the octagons after it
    // in its row move along by the difference, and it moves half of it.

    private ActionToken _bookAction;
    private BookView _book;
    private float _bookExtra; // how much wider than a book
    private ResourceBundle _bookContent, _bookBelow;

    private Vector2 BonusPlace(int place, bool book)
    {
        int n = (book ? 1 : 0) + place;
        bool firstRow = BonusActionsPerRow <= 0 || n < BonusActionsPerRow;
        return BonusActionOffset(n) + (book && firstRow ? new Vector2(_bookExtra + 2 * BookPadding, 0) : Vector2.Zero);
    }

    private void ShowBookAction(BookView book)
    {
        if (book == _book && (_bookAction is not null) == (book is not null))
            return;
        if (_book is not null && book is not null && _bookAction is not null && IsInstanceValid(_bookAction)
            && book with { Content = null, Below = null } == _book with { Content = null, Below = null })
        {
            _book = book; // only the amounts changed
            ShowBookAmounts(book);
            return;
        }
        _bookAction?.QueueFree();
        _bookAction = null;
        _bookContent = _bookBelow = null;
        _book = book;
        _bookExtra = 0;
        if (book is null)
            return;
        _bookAction = ActionToken.Create();
        _bookAction.Name = "BookAction";
        _bonusActionRow.AddChild(_bookAction);
        int size = book.Backing == Icon.Safe ? SafeIconSize : Art.BookSize;
        _bookExtra = Mathf.Max(0, IconArt.Texture(book.Backing, size).GetWidth() - Art.BookActionBacking().GetWidth());
        _bookAction.Position = BonusActionOffset(0) + new Vector2(_bookExtra / 2 + BookPadding, 0);
        if (_bookAction.Octagon is IconSprite drawn)
        {
            drawn.Icon = book.Backing;
            drawn.IconSize = size;
        }
        else if (_bookAction.Octagon is Sprite2D plain)
            plain.Texture = IconArt.Texture(book.Backing, size);
        if (book.Picture is not null)
            _bookAction.ShowIcon(book.Picture);
        ShowBookAmounts(book);
    }

    // What the book shows as amounts: on its panel (or in the Safe), and under it.
    private void ShowBookAmounts(BookView book)
    {
        if (book.Content is not null)
        {
            Amounts content = _inFlight.GetValueOrDefault(ResourceKind.Treasure) is int flying and not 0
                ? book.Content + Amounts.Of(ResourceKind.Treasure, -flying) // still flying to it
                : book.Content;
            bool count = book.Backing == Icon.BookAction && !book.Income; // the Djinni's lamps, the Goblins' treasures
            _bookContent ??= AddBookBundle("BookContent",
                count ? BookCountIconSize : book.Income ? BookIncomeIconSize : BookContentIconSize,
                count ? BookCountFontSize : BookContentFontSize,
                book.Income ? BookContentSpacing * BookIncomeIconSize / BookContentIconSize : BookContentSpacing, BookContentOffset);
            _bookContent.IncomeIcons = book.Income;
            _bookContent.OneHand = book.Income;
            if (_bookContent.Kinds != (book.ContentKinds ?? ""))
                _bookContent.Kinds = book.ContentKinds ?? "";
            _bookContent.ShowAmounts(content);
        }
        if (_bookContent is not null)
            _bookContent.Visible = book.Content is not null;
        if (book.Below is not null)
        {
            _bookBelow ??= AddBookBundle("BookBelow", BookBelowIconSize, BookBelowFontSize, BookBelowSpacing, BookBelowOffset);
            _bookBelow.ShowAmounts(book.Below);
        }
        if (_bookBelow is not null)
            _bookBelow.Visible = book.Below is not null;
    }

    private ResourceBundle AddBookBundle(string name, int iconSize, int fontSize, float spacing, Vector2 offset)
    {
        var bundle = new ResourceBundle
        {
            Name = name, Centred = true, ShowOnes = true, IconSize = iconSize, FontSize = fontSize, Spacing = spacing,
            Position = offset,
        };
        _bookAction.AddChild(bundle);
        _bookAction.MoveChild(_bookAction.Used, -1);
        return bundle;
    }

    // Which kind on the book (in the Safe) is under the point, if any.
    public ResourceKind? BookKindAt(Vector2 globalPoint) => KindAt(_bookContent, globalPoint);

    private ResourceKind? KindAt(ResourceBundle bundle, Vector2 globalPoint) =>
        Visible && bundle is not null && IsInstanceValid(bundle) && bundle.IsVisibleInTree()
            ? bundle.Shown.Parts.Select(part => (ResourceKind?)part.Kind)
                .FirstOrDefault(kind => bundle.IconFor(kind.Value) is Sprite2D icon && icon.Texture is not null
                    && icon.GetRect().HasPoint(icon.ToLocal(globalPoint)))
            : null;

    // ---------- Treasurers: what may go into the Safe ----------
    private ResourceBundle _safeOffer;
    private Button _safeAll;

    public void ShowSafeOffer(Amounts left)
    {
        bool shown = left is not null && _bookAction is not null && _book?.Backing == Icon.Safe && !IsCompact;
        if (shown && (_safeOffer is null || !IsInstanceValid(_safeOffer) || _safeOffer.GetParent() != _bookAction))
        {
            _safeOffer = new ResourceBundle
            {
                Name = "SafeOffer", Centred = true, ShowOnes = true, IconSize = SafeOfferIconSize, ZIndex = 5,
                Spacing = SafeOfferIconSize * 1.15f, Position = SafeOfferOffset, FontSize = BookContentFontSize,
            };
            _bookAction.AddChild(_safeOffer);
            _safeAll = new Button
            {
                Name = "SafeAll", Text = "All", MouseFilter = Control.MouseFilterEnum.Ignore, // clicked through Main
                Position = SafeAllOffset - SafeAllSize / 2, Size = SafeAllSize, ZIndex = 5,
            };
            _bookAction.AddChild(_safeAll);
        }
        if (_safeOffer is not null && IsInstanceValid(_safeOffer))
        {
            _safeOffer.Visible = shown;
            _safeAll.Visible = shown && !left.IsEmpty;
            if (shown)
                _safeOffer.ShowAmounts(left);
        }
    }

    public ResourceKind? SafeOfferKindAt(Vector2 globalPoint) => KindAt(_safeOffer, globalPoint);

    public bool IsOverSafeAll(Vector2 globalPoint) =>
        Visible && _safeAll is not null && IsInstanceValid(_safeAll) && _safeAll.IsVisibleInTree()
        && _safeAll.GetGlobalRect().HasPoint(globalPoint);

    // ---------- Treasurers: the Safe's editor (Setup) ----------
    public static readonly ResourceKind[] SafeKinds = { ResourceKind.Priest, ResourceKind.Worker, ResourceKind.Coin };
    private Node2D _safeEditor;
    private readonly Sprite2D[] _safeEditorIcons = new Sprite2D[3];

    public bool SafeEditorShown => _safeEditor is not null && IsInstanceValid(_safeEditor) && _safeEditor.IsVisibleInTree();

    // Over the Safe itself (the Treasurers' book).
    public bool IsOverSafe(Vector2 globalPoint) => _book?.Backing == Icon.Safe && IsOverBookAction(globalPoint);

    // The editor under the Safe (made the first time, again if the Safe was rebuilt); false hides it.
    public void ShowSafeEditor(bool on)
    {
        bool can = on && _bookAction is not null && IsInstanceValid(_bookAction) && _book?.Backing == Icon.Safe && !IsCompact;
        if (can && (_safeEditor is null || !IsInstanceValid(_safeEditor) || _safeEditor.GetParent() != _bookAction))
            BuildSafeEditor();
        if (_safeEditor is not null && IsInstanceValid(_safeEditor))
            _safeEditor.Visible = can;
    }

    // In the Safe's pixels: centred under it.
    private Rect2 SafeEditorRect()
    {
        var size = new Vector2(2 * SafeEditorPadding.X + (SafeKinds.Length - 1) * SafeEditorSpacing + SafeEditorIconSize,
            2 * SafeEditorPadding.Y + SafeEditorIconSize);
        return new Rect2(new Vector2(-size.X / 2, SafeIconSize / 2f + SafeEditorGap), size);
    }

    private void BuildSafeEditor()
    {
        Rect2 rect = SafeEditorRect();
        _safeEditor = new Node2D { Name = "SafeEditor", ZIndex = SafeEditorZIndex };
        PowerBowlsView look = PowerBank; // drawn as its conversions' menu
        Vector2[] outline = HighlightArt.RoundRect(rect, look?.ConversionCornerRadius ?? 14f);
        _safeEditor.AddChild(new Polygon2D { Polygon = outline, Color = look?.ConversionFillColour ?? new Color(0.055f, 0.055f, 0.055f, 0.9f) });
        _safeEditor.AddChild(new Line2D
        {
            Points = outline.Append(outline[0]).ToArray(), Width = look?.ConversionEdgeWidth ?? 2f,
            DefaultColor = look?.ConversionEdgeColour ?? new Color(0.62f, 0.45f, 0.78f),
        });
        for (var i = 0; i < SafeKinds.Length; i++)
        {
            _safeEditorIcons[i] = new Sprite2D
            {
                Texture = Art.ResourceIcon(SafeKinds[i], size: SafeEditorIconSize),
                Position = new Vector2(rect.GetCenter().X + (i - (SafeKinds.Length - 1) / 2f) * SafeEditorSpacing, rect.GetCenter().Y),
            };
            _safeEditor.AddChild(_safeEditorIcons[i]);
        }
        _bookAction.AddChild(_safeEditor);
    }

    // The kind whose icon is under the point, or null.
    public ResourceKind? SafeEditorKindAt(Vector2 globalPoint)
    {
        if (!SafeEditorShown)
            return null;
        Vector2 local = _safeEditor.ToLocal(globalPoint);
        for (var i = 0; i < SafeKinds.Length; i++)
            if (new Rect2(_safeEditorIcons[i].Position - Vector2.One * SafeEditorIconSize / 2f, Vector2.One * SafeEditorIconSize)
                    .Grow((SafeEditorSpacing - SafeEditorIconSize) / 2f).HasPoint(local))
                return SafeKinds[i];
        return null;
    }

    public bool IsOverSafeEditor(Vector2 globalPoint) =>
        SafeEditorShown && SafeEditorRect().HasPoint(_safeEditor.ToLocal(globalPoint));

    // The book (or Safe) itself, in the panel's pixels: where a resource flies to it (Goblins' treasures, the Safe).
    public Node2D BookNode => _bookAction is not null && IsInstanceValid(_bookAction) ? _bookAction : null;

    public bool IsOverBookAction(Vector2 globalPoint) =>
        Visible && _bookAction is not null && _bookAction.Visible && _bookAction.IsOver(globalPoint);

    // The bonus action whose octagon is under the point, if any.
    public BonusActionId? BonusActionAt(Vector2 globalPoint) =>
        !Visible
            ? null
            : _octagons.Where(pair => pair.Value.IsOver(globalPoint))
                .Select(pair => (BonusActionId?)pair.Key)
                .FirstOrDefault();
}
