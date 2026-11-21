using Core;
using Godot;

namespace TMCalc;

public enum NeutralPiece { Priest, Bridge, Dwelling }

// Calculator: the neutral opponent's bank, where the other players' boards would be: a small framed bar in the top
// panel's look (its stone fill, frame in the power purple), holding one priest, one bridge and one dwelling in the power
// purple, each an endless stack (Main drags copies out of it: MainNeutral.cs). Drawn in its own pixels; Main scales it
// like the player boards. Made in code.
[GlobalClass]
public partial class NeutralBank : Node2D
{
    [Export] public Vector2 BankSize { get => _size; set { _size = value; QueueRedraw(); } }
    [Export] public float FrameWidth { get; set; } = 7.8f;
    [Export] public float CornerRadius { get; set; } = 24f;
    [Export] public float PieceScale { get; set; } = 1f;

    private Vector2 _size = new(420, 150);
    private static readonly NeutralPiece[] Order = { NeutralPiece.Priest, NeutralPiece.Bridge, NeutralPiece.Dwelling };

    public static Color Purple => IconArt.PowerPurple;
    public static Color PieceOutline => Art.OutlineFor(PlayerColour.Black);

    public static Texture2D Texture(NeutralPiece piece) => piece switch
    {
        NeutralPiece.Priest => PieceArt.TextureFor(PieceKind.Priest, Purple, PieceOutline),
        NeutralPiece.Dwelling => PieceArt.TextureFor(PieceKind.Dwelling, Purple, PieceOutline),
        _ => HexArt.BridgeIcon(Purple, PieceOutline),
    };

    // Where each piece sits, in this node's pixels (the bank's middle is the origin).
    public Vector2 Place(NeutralPiece piece) =>
        new(((int)System.Array.IndexOf(Order, piece) - 1) * _size.X / 3.2f, 0);

    public NeutralPiece? PieceAt(Vector2 globalPoint)
    {
        if (!IsVisibleInTree())
            return null;
        Vector2 local = ToLocal(globalPoint);
        foreach (NeutralPiece piece in Order)
            if (local.DistanceTo(Place(piece)) <= _size.Y * 0.4f)
                return piece;
        return null;
    }

    public bool IsOver(Vector2 globalPoint) =>
        IsVisibleInTree() && new Rect2(-_size / 2, _size).HasPoint(ToLocal(globalPoint));

    public override void _Draw()
    {
        var box = new StyleBoxFlat
        {
            BgColor = FactionBoardArt.SectionColour,
            BorderColor = Purple,
            CornerDetail = 8,
        };
        box.SetBorderWidthAll(Mathf.RoundToInt(FrameWidth));
        box.SetCornerRadiusAll(Mathf.RoundToInt(CornerRadius));
        DrawStyleBox(box, new Rect2(-_size / 2, _size));
        foreach (NeutralPiece piece in Order)
        {
            Texture2D texture = Texture(piece);
            Vector2 size = texture.GetSize() * PieceScale;
            DrawTextureRect(texture, new Rect2(Place(piece) - size / 2, size), false);
        }
    }
}
