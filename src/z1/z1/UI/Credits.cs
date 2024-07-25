namespace z1.UI;

internal sealed class CreditsType
{
    public const int StartY = Global.StdViewHeight;

    private const int AllLines = 96;
    private const int AllLineBytes = 12;

    private TableResource<byte> textTable;
    private byte[] lineBmp = new byte[AllLineBytes];
    private int fraction;
    private int tileOffset;
    private int top;
    private int windowTop;
    private int windowTopLine;
    private int windowBottomLine;
    private int windowFirstMappedLine;
    private byte[] playerLine = new byte[32];
    private bool madePlayerLine;

    public bool IsDone() => throw new NotImplementedException();
    public int GetTop() => throw new NotImplementedException();

    public void Update() => throw new NotImplementedException();
    public void Draw() => throw new NotImplementedException();
}
