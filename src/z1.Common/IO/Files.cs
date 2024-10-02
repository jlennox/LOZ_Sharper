namespace z1.Common.IO;

public static class Filenames
{
    public const string GuiFont = "UbuntuMono-Regular.ttf";
    public const string WindowIcon = "icon.ico";

    public const string Font = "font.png";
    public const string FontAddendum = "font-addendum.png";

    public static string FormatLevelWorld(int quest, int level) => $"Level{quest:D2}_{level:D2}.world";
    public static string FormatLevelAnyQuest(int level) => $"Level{{QuestId}}_{level:D2}.world";
}

public sealed class LevelDirectory
{
    public string LevelInfoBlock { get; set; }
    public string RoomCols { get; set; }
    public string ColTables { get; set; }
    public string TileAttrs { get; set; }
    public string TilesImage { get; set; }
    public string PlayerImage { get; set; }
    public string PlayerSheet { get; set; }
    public string RoomAttrs { get; set; }
    public string ObjLists { get; set; }
    public string Extra1 { get; set; }
}