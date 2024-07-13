namespace z1.UI;

internal sealed class ProfileSummary
{
    public byte[] Name = new byte[PlayerProfile.MaxNameLength];
    public int NameLength;
    public byte Quest;
    public byte Deaths;
    public byte HeartContainers;

    public bool IsActive() => Name != null && Name.Length > 0;
}

internal sealed class ProfileSummarySnapshot
{
    public ProfileSummary[] Summaries =
        Enumerable.Range(0, SaveFolder.MaxProfiles).Select(_ => new ProfileSummary()).ToArray();
}

internal static class SaveFolder
{
    public const int MaxProfiles = 3;

    // Make this into LoadSummaries and do not take an argument?
    // TODO
    public static ProfileSummarySnapshot ReadSummaries() => new ProfileSummarySnapshot();
    public static PlayerProfile ReadProfile(int index) => new PlayerProfile();

    public static bool WriteProfile(int index, PlayerProfile profile)
    {
        return true;
    } // TODOthrow new NotImplementedException();
}