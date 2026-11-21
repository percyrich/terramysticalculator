using Core;
using Godot;

namespace TMCalc;

// One bookmark in the bookmark map's list (HistoryOverlay makes them: new BookmarkRow(), then Setup): where the player
// is (the knob, this row only; the row lit, the name bolder), the gold ribbon, the name (a text box over it while
// renaming), the pencil and the bin (neither for Session start). Lit while it or its ribbon on the map is hovered;
// pressing it jumps there. Panel pixels.
public partial class BookmarkRow : Control
{
    public Bookmark Bookmark { get; private set; }
    public bool Lit { get => _lit; set { _lit = value; QueueRedraw(); } }

    private HistoryOverlay _owner;
    private bool _here, _sessionStart;
    private int _indent;
    private readonly Label _name = new() { VerticalAlignment = VerticalAlignment.Center, ClipText = true,
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, MouseFilter = MouseFilterEnum.Ignore };
    private readonly LineEdit _edit = new() { Visible = false, SelectAllOnFocus = true, CaretBlink = true };
    private readonly TextureButton _pencil = new(), _bin = new();
    private bool _lit;

    public void Setup(HistoryOverlay owner, Bookmark bookmark, int indent, bool here, bool sessionStart)
    {
        _owner = owner;
        Bookmark = bookmark;
        _indent = indent;
        _here = here;
        _sessionStart = sessionStart;
        CustomMinimumSize = new Vector2(0, owner.RowHeight);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = $"Go to {bookmark.Name}";
        _name.Text = bookmark.Name;
        _name.LabelSettings = new LabelSettings
        {
            Font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = here ? 0.3f : -0.3f },
            FontSize = owner.FontSize,
            FontColor = owner.TextColour,
        };
        AddChild(_name);

        // the name box while renaming: the section's dark, edged in the frame colour, the name's own size
        var box = new StyleBoxFlat { BgColor = TopPanelArt.SectionColour, BorderColor = owner.FrameColour };
        box.SetBorderWidthAll(2);
        box.SetCornerRadiusAll(6);
        box.ContentMarginLeft = box.ContentMarginRight = 6;
        foreach (string style in new[] { "normal", "focus", "read_only" })
            _edit.AddThemeStyleboxOverride(style, box);
        _edit.AddThemeFontSizeOverride("font_size", owner.FontSize);
        _edit.AddThemeColorOverride("font_color", new Color("f2f6f9"));
        _edit.AddThemeColorOverride("caret_color", new Color("f2f6f9"));
        AddChild(_edit);
        _edit.TextSubmitted += _ => owner.EndRename(keep: true);
        _edit.FocusExited += () => { if (owner.IsEditing(this)) owner.EndRename(keep: true); };
        _edit.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                _edit.AcceptEvent();
                owner.EndRename(keep: false);
            }
        };

        if (!sessionStart)
        {
            Dress(_pencil, TopPanelArt.RowIconKind.Pencil, $"Rename {bookmark.Name}");
            _pencil.Pressed += () => owner.StartRename(this);
            Dress(_bin, TopPanelArt.RowIconKind.Bin, $"Delete {bookmark.Name}");
            _bin.Pressed += () => owner.RequestDelete(Bookmark);
        }
        MouseEntered += () => owner.HoverRow(PositionOf());
        MouseExited += () => { if (owner.IsListHovered(PositionOf())) owner.HoverRow(null); };
        Resized += Relayout;
    }

    private int PositionOf() => Bookmark.Position;

    // A row icon as a flat button: dimmed until the pointer is on it.
    private void Dress(TextureButton button, TopPanelArt.RowIconKind kind, string tip)
    {
        int size = _owner.RowIconSize;
        button.TextureNormal = TopPanelArt.RowIcon(kind, false, size);
        button.IgnoreTextureSize = true;
        button.StretchMode = TextureButton.StretchModeEnum.KeepCentered;
        button.CustomMinimumSize = new Vector2(size + 4, size + 4);
        button.Size = button.CustomMinimumSize;
        button.FocusMode = FocusModeEnum.None;
        button.MouseDefaultCursorShape = CursorShape.PointingHand;
        button.TooltipText = tip;
        button.SelfModulate = new Color(1, 1, 1, 0.6f);
        button.MouseEntered += () => { button.SelfModulate = Colors.White; _owner.HoverRow(PositionOf()); };
        button.MouseExited += () => button.SelfModulate = new Color(1, 1, 1, 0.6f);
        AddChild(button);
    }

    private float NameX => 12 + _indent * _owner.IndentWidth + 50;
    private float PencilX => Size.X - 26 - 30;

    private void Relayout()
    {
        float h = Size.Y, icon = _owner.RowIconSize + 4;
        _bin.Position = new Vector2(Size.X - 26 - icon / 2, (h - icon) / 2);
        _pencil.Position = new Vector2(PencilX - icon / 2, (h - icon) / 2);
        float right = _sessionStart ? Size.X - 12 : PencilX - 18;
        _name.Position = new Vector2(NameX, 0);
        _name.Size = new Vector2(Mathf.Max(0, right - NameX), h);
        _edit.Position = new Vector2(NameX - 6, 3);
        _edit.Size = new Vector2(Mathf.Max(0, right + 4 - NameX + 6), h - 6);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_here || _lit)
            DrawStyleBox(Fill(_here ? _owner.CurrentRowColour : _owner.HoverRowColour), new Rect2(4, 2, Size.X - 8, Size.Y - 4));
        float x = 12 + _indent * _owner.IndentWidth, cy = Size.Y / 2;
        if (_here)
        {
            Texture2D knob = TopPanelArt.HereKnob(9, _owner.FrameColour);
            DrawTexture(knob, new Vector2(x + 10, cy) - knob.GetSize() / 2);
        }
        Texture2D ribbon = TopPanelArt.Ribbon(true, _lit, _owner.FrameColour, _owner.RowIconSize);
        DrawTexture(ribbon, new Vector2(x + 32, cy) - ribbon.GetSize() / 2);
    }

    private static StyleBoxFlat Fill(Color colour)
    {
        var box = new StyleBoxFlat { BgColor = colour };
        box.SetCornerRadiusAll(6);
        return box;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } && !_edit.Visible)
        {
            AcceptEvent();
            _owner.RequestJump(Bookmark);
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left })
            AcceptEvent();
    }

    public void StartEditing()
    {
        _edit.Text = _name.Text;
        _name.Visible = false;
        _edit.Visible = true;
        _edit.GrabFocus();
        _edit.SelectAll();
    }

    // The new name if it is to be kept (and differs and isn't empty), null otherwise; the row shows the name it ends
    // with until the list is rebuilt.
    public string StopEditing(bool keep)
    {
        string name = _edit.Text.Trim();
        _edit.Visible = false;
        _name.Visible = true;
        if (!keep || name.Length == 0 || name == Bookmark.Name)
        {
            _name.Text = Bookmark.Name;
            return null;
        }
        _name.Text = name;
        return name;
    }
}
