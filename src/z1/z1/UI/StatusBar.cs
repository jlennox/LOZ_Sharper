namespace z1.UI;

public enum StatusBarFeatures
{
    None = 0,
    Counters = 1,
    Equipment = 2,
    MapCursors = 4,

    All = Counters | Equipment | MapCursors,
    EquipmentAndMap = Equipment | MapCursors,
}

internal sealed class StatusBar
{
    public const int StatusBarHeight = 0x40;

    public StatusBarFeatures features;

    public void EnableFeatures(StatusBarFeatures features, bool enable)
    {
        features = enable ? (this.features | features) : (this.features ^ features);
    }

    public void Draw(int baseY)
    {
        // ALLEGRO_COLOR backColor = al_map_rgb(0, 0, 0);
        // Draw(baseY, backColor);
    }
}
