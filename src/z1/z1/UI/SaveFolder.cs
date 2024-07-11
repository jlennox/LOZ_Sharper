namespace z1.UI;

internal sealed class ProfileSummary
{
    public string Name; // byte Name[MaxNameLength];
    public byte Quest;
    public byte Deaths;
    public byte HeartContainers;

    public bool IsActive() => Name != null && Name.Length > 0;
}

internal sealed class ProfileSummarySnapshot
{
    public ProfileSummary[] Summaries = new ProfileSummary[SaveFolder.MaxProfiles];
}

internal static class SaveFolder
{
    public const int MaxProfiles = 3;

    // Make this into LoadSummaries and do not take an argument?
    public static void ReadSummaries(ProfileSummarySnapshot summaries) => throw new NotImplementedException();
    public static bool ReadProfile(int index, PlayerProfile profile) => throw new NotImplementedException();
    public static bool WriteProfile(int index, PlayerProfile profile) => throw new NotImplementedException();
}