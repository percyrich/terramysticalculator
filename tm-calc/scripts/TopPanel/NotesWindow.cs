using Godot;

namespace TMCalc;

// The notes (the bookmark map's "Open notes" key): a window of its own, outside the game's (ForceNative), so it can be
// moved to another screen, holding a TopPanelLog that lists History.Notes oldest first (Ascending): the game line
// whole, the lines leaving it indented under where they part, "(Alternate)" before each line's first record. Its
// buttons are the log's (Log.BookmarkToggled, Log.JumpRequested: the row's index). Closing it only hides it.
// Godot can only give it a window of its own when the game itself runs in one: run from the editor with the Game tab's
// "Embed Game on Next Play" on (Engine.IsEmbeddedInEditor), every window is drawn inside the game's instead
// (IsEmbedded). Then it is kept wholly inside the game's window, so it can't be lost off its edge, and Main says why.
// Its place and size are remembered in user://notes_window.cfg (checked twice a second while it is shown), separately
// for a window of its own (screen pixels; used again if still on a screen) and one inside the game's.
public partial class NotesWindow : Window
{
    public TopPanelLog Log { get; } = new() { Name = "Notes", Ascending = true };

    private const string ConfigPath = "user://notes_window.cfg", Section = "window";
    private static readonly Vector2I DefaultSize = new(760, 1000);
    private static readonly Color BackColour = new("141d27");
    private Vector2I _savedPosition, _savedSize;
    private double _sinceCheck;

    public NotesWindow()
    {
        Name = "NotesWindow";
        Title = "Terra Mysticalculator: notes";
        // Hidden first: a new Window is visible, and Godot refuses to change ForceNative on a visible window (it stays
        // off, with only an error in the output, and the window is drawn inside the game's).
        Visible = false;
        ForceNative = true;   // a window of the system's, not drawn inside the game's
        Transient = false;    // free to go anywhere, another screen included; listed on its own (Alt+Tab)
        Exclusive = false;
        Unresizable = false;
        MinSize = new Vector2I(360, 300);
        CloseRequested += () =>
        {
            Remember();
            Hide();
        };
    }

    // Drawn inside the game's window (not a window of its own).
    public bool Inside => IsInsideTree() && IsEmbedded();

    public override void _Ready()
    {
        var back = new ColorRect { Name = "Back", Color = BackColour, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(back);
        back.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(Log);
        Log.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    // Shows it (where it was last, if it was) and brings it to the front.
    public void Open()
    {
        if (!Visible)
        {
            Restore();
            Show();
        }
        KeepInside();
        GrabFocus(); // brings it to the front (MoveToForeground is obsolete)
    }

    public override void _Process(double delta)
    {
        if (!Visible)
            return;
        KeepInside();
        _sinceCheck += delta;
        if (_sinceCheck < 0.5)
            return;
        _sinceCheck = 0;
        if (Position != _savedPosition || Size != _savedSize)
            Remember();
    }

    public override void _ExitTree() => Remember();

    // Inside the game's window: wholly within it (smaller if need be), title bar included.
    private void KeepInside()
    {
        if (!Inside)
            return;
        Rect2I view = new((Vector2I)GetTree().Root.GetVisibleRect().Position, (Vector2I)GetTree().Root.GetVisibleRect().Size);
        int title = GetThemeConstant("title_height");
        var size = new Vector2I(Mathf.Min(Size.X, view.Size.X), Mathf.Min(Size.Y, view.Size.Y - title));
        if (size != Size)
            Size = size;
        var at = new Vector2I(Mathf.Clamp(Position.X, view.Position.X, view.End.X - Size.X),
            Mathf.Clamp(Position.Y, view.Position.Y + title, view.End.Y - Size.Y));
        if (at != Position)
            Position = at;
    }

    private string Key(string name) => Inside ? $"inside_{name}" : name;

    private void Remember()
    {
        if (!Visible)
            return;
        _savedPosition = Position;
        _savedSize = Size;
        var config = new ConfigFile();
        config.Load(ConfigPath); // keeps the other kind's place
        config.SetValue(Section, Key("position"), Position);
        config.SetValue(Section, Key("size"), Size);
        config.Save(ConfigPath);
    }

    private void Restore()
    {
        var config = new ConfigFile();
        if (config.Load(ConfigPath) == Error.Ok && config.HasSectionKey(Section, Key("position")) && config.HasSectionKey(Section, Key("size")))
        {
            var place = new Rect2I(config.GetValue(Section, Key("position")).AsVector2I(), config.GetValue(Section, Key("size")).AsVector2I());
            bool onScreen = Inside; // (kept inside the game's window anyway)
            for (var screen = 0; !onScreen && screen < DisplayServer.GetScreenCount(); screen++)
                onScreen = DisplayServer.ScreenGetUsableRect(screen).Intersects(new Rect2I(place.Position, new Vector2I(80, 40)));
            if (onScreen && place.Size.X >= MinSize.X && place.Size.Y >= MinSize.Y)
            {
                Size = place.Size;
                Position = place.Position;
                _savedPosition = Position;
                _savedSize = Size;
                return;
            }
        }
        // Centred on the game's screen (or in the game's window), as big as fits.
        Rect2I usable = Inside
            ? new Rect2I((Vector2I)GetTree().Root.GetVisibleRect().Position, (Vector2I)GetTree().Root.GetVisibleRect().Size)
            : DisplayServer.ScreenGetUsableRect(GetTree().Root.CurrentScreen);
        Size = new Vector2I(Mathf.Min(DefaultSize.X, usable.Size.X), Mathf.Min(DefaultSize.Y, usable.Size.Y - 60));
        Position = usable.Position + (usable.Size - Size) / 2;
    }
}
