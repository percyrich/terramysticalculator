using System;
using Godot;

namespace TMCalc;

// A message box in the top panel's style (the bookmark map's "BGA Bookmarklet", and what Load BGA JSON has to say):
// centred on the screen over a dimmed screen, the top panel's frame and stone, a title, wrapped text, optionally a
// sunken box of selectable text (read only; Ctrl+A, Ctrl+C and the right-click menu work), optionally one stone key
// along the bottom, and an X key at the top right. The X, Esc (Main._Input) or a click on the dimmed screen closes it.
// Sized in panel pixels and scaled like the top panel (Open's scale), smaller if the screen is too small for it.
// Main puts it on its own CanvasLayer above the top panel and the bookmark map, so whatever is open underneath stays
// open when it closes.
[GlobalClass]
public partial class TopPanelModal : Control
{
    [ExportGroup("Layout")]
    [Export] public float Width { get; set; } = 1000f;         // panel pixels
    [Export] public float Padding { get; set; } = 28f;         // inside the frame
    [Export] public int TitleFontSize { get; set; } = 30;
    [Export] public int FontSize { get; set; } = 23;
    [Export] public int CodeFontSize { get; set; } = 18;
    [Export] public float CodeHeight { get; set; } = 300f;
    [Export] public float KeyHeight { get; set; } = 50f;
    [Export] public float KeyWidth { get; set; } = 300f;
    [Export] public float CloseSize { get; set; } = 46f;
    [Export] public float Gap { get; set; } = 18f;              // between the title, the text, the box and the key
    [Export] public float ScreenMargin { get; set; } = 0.92f;   // at most this share of the screen's width and height
    [ExportGroup("Colours")]
    [Export] public Color DimColour { get; set; } = new(0, 0, 0, 0.55f);
    [Export] public Color TitleColour { get; set; } = new("f7fafc");
    [Export] public Color TextColour { get; set; } = new("e4ebf1");
    [Export] public Color CodeColour { get; set; } = new("cfe0ee");

    public bool IsOpen => Visible;
    public event Action Closed;

    private const float Frame = TopPanelArt.Frame;
    private readonly Control _drawer = new() { Name = "Drawer", MouseFilter = MouseFilterEnum.Stop };
    private readonly TextureRect _backing = new()
    {
        Name = "Backing", MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.Scale,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
    };
    private readonly Label _title = new() { Name = "Title", MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _body = new() { Name = "Body", MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly TextEdit _code = new()
    {
        Name = "Code", Editable = false, WrapMode = TextEdit.LineWrappingMode.Boundary, ContextMenuEnabled = true,
        SelectingEnabled = true, ShortcutKeysEnabled = true, DeselectOnFocusLossEnabled = false,
    };
    private readonly TopPanelTextKey _key = new() { Name = "Key" };
    private readonly TopPanelTextKey _close = new() { Name = "Close", Text = "X", TooltipText = "Close (Esc)" };
    private Action _keyPressed;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop; // the dimmed screen takes the clicks
        var dim = new ColorRect { Name = "Dim", Color = DimColour, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_drawer);
        _drawer.AddChild(_backing);
        _drawer.AddChild(_title);
        _drawer.AddChild(_body);
        _drawer.AddChild(_code);
        _drawer.AddChild(_key);
        _drawer.AddChild(_close);
        var clear = new StyleBoxEmpty();
        foreach (string box in new[] { "normal", "focus", "read_only" })
            _code.AddThemeStyleboxOverride(box, clear);
        _code.AddThemeColorOverride("font_readonly_color", CodeColour);
        _code.AddThemeColorOverride("font_color", CodeColour);
        _code.AddThemeColorOverride("selection_color", new Color(0.45f, 0.6f, 0.85f, 0.55f));
        _code.AddThemeFontSizeOverride("font_size", CodeFontSize);
        _key.Pressed += () => _keyPressed?.Invoke();
        _close.Pressed += () => Callable.From((Action)Close).CallDeferred();
        Visible = false;
    }

    // Shows the box: title, text (blank lines between paragraphs), the selectable `code` (null: none), and a key
    // (`keyText` null: none) calling keyPressed. frame and scale: the top panel's.
    public void Open(string title, string body, Color frame, float scale, string code = null, string keyText = null, Action keyPressed = null)
    {
        _title.Text = title ?? "";
        _title.LabelSettings = new LabelSettings
        {
            Font = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.5f },
            FontSize = TitleFontSize, FontColor = TitleColour,
            ShadowColor = new Color(0, 0, 0, 0.6f), ShadowSize = 2, ShadowOffset = new Vector2(0, 1),
        };
        _body.Text = body ?? "";
        _body.LabelSettings = new LabelSettings { Font = ThemeDB.FallbackFont, FontSize = FontSize, FontColor = TextColour, LineSpacing = 4 };
        _code.Visible = code is not null;
        _code.Text = code ?? "";
        _code.ScrollVertical = 0;
        _key.Visible = keyText is not null;
        _key.Text = keyText ?? "";
        _key.FontSize = 21;
        _keyPressed = keyPressed;
        Visible = true;
        Place(frame, scale);
    }

    public void SetKeyText(string text) => _key.Text = text ?? "";

    public void Close()
    {
        if (!Visible)
            return;
        Visible = false;
        _keyPressed = null;
        Closed?.Invoke();
    }

    // A click on the dimmed screen, outside the box (the box stops clicks inside it), closes it.
    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle })
        {
            AcceptEvent();
            Callable.From((Action)Close).CallDeferred();
        }
    }

    private void Place(Color frame, float scale)
    {
        float x0 = Frame + Padding, inner = Width - 2 * x0, y = Frame + Padding * 0.8f;
        _title.Position = new Vector2(x0, y);
        _title.Size = new Vector2(inner - CloseSize - Gap, TitleFontSize * 1.4f);
        _close.Size = new Vector2(CloseSize, CloseSize);
        _close.Position = new Vector2(Width - Frame - Padding * 0.6f - CloseSize, Frame + Padding * 0.6f);
        y += Mathf.Max(TitleFontSize * 1.4f, CloseSize * 0.6f) + Gap;

        float bodyHeight = 0;
        if (_body.Text.Length > 0)
        {
            Vector2 measured = ThemeDB.FallbackFont.GetMultilineStringSize(_body.Text, HorizontalAlignment.Left, inner, FontSize);
            int lines = Mathf.Max(1, Mathf.RoundToInt(measured.Y / ThemeDB.FallbackFont.GetHeight(FontSize)));
            bodyHeight = measured.Y + lines * 4 + 6; // LineSpacing 4, and a little slack
        }
        _body.Position = new Vector2(x0, y);
        _body.Size = new Vector2(inner, bodyHeight);
        y += bodyHeight + (bodyHeight > 0 ? Gap : 0);

        Rect2? codeArea = null;
        if (_code.Visible)
        {
            codeArea = new Rect2(x0, y, inner, CodeHeight);
            _code.Position = codeArea.Value.Position + new Vector2(10, 8);
            _code.Size = codeArea.Value.Size - new Vector2(20, 16);
            y += CodeHeight + Gap;
        }
        if (_key.Visible)
        {
            _key.Size = new Vector2(KeyWidth, KeyHeight);
            _key.Position = new Vector2((Width - KeyWidth) / 2, y);
            y += KeyHeight + Gap;
        }
        float height = y - Gap + Padding + Frame;
        var size = new Vector2(Width, height);
        _drawer.CustomMinimumSize = size;
        _drawer.Size = size;
        _backing.Texture = codeArea is Rect2 area ? TopPanelArt.DrawerBacking(size, frame, (area, 12)) : TopPanelArt.DrawerBacking(size, frame);
        _backing.Size = size;

        Vector2 screen = GetViewportRect().Size;
        scale = Mathf.Min(scale, Mathf.Min(screen.X * ScreenMargin / size.X, screen.Y * ScreenMargin / size.Y));
        _drawer.Scale = Vector2.One * scale;
        _drawer.Position = ((screen - size * scale) / 2).Round();
    }
}
