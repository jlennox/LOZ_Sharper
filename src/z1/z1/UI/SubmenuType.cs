namespace z1.UI;

internal sealed class SubmenuType
{
    public const int Width = Global.StdViewWidth;
    public const int Height = 0xAE;
    public const int ActiveItems = 8;
    public const int PassiveItems = 6;

    private bool enabled;
    private bool activated;
    private int activeUISlot;
    private int[] activeSlots = new int[ActiveItems];
    private int[] activeItems = new int[ActiveItems];
    // private SpriteImage cursor;

    public void Enable() { throw new NotImplementedException(); }
    public void Disable() => enabled = false;
    public void Activate() => activated = true;
    public void Deactivate() => activated = false;

    public void Update() => throw new NotImplementedException();
    public void Draw(int bottom) => throw new NotImplementedException();
}