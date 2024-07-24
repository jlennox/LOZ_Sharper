using System.Diagnostics.PerformanceData;

namespace z1;

internal enum SoundEffect
{
    Sea,
    SwordWave,
    BossHit,
    Door,
    PlayerHit,
    BossRoar1,
    BossRoar2,
    BossRoar3,
    Cursor,
    RoomItem,
    Secret,
    Item,
    MonsterDie,
    Sword,
    Boomerang,
    Fire,
    Stairs,
    Bomb,
    Parry,
    MonsterHit,
    MagicWave,
    KeyHeart,
    Character,
    PutBomb,
    LowHp,

    MAX
}

internal enum SongId
{
    Intro,
    Ending,
    Overworld,
    Underworld,
    ItemLift,
    Triforce,
    Ganon,
    Level9,
    GameOver,
    Death,
    Recorder,
    Zelda,

    MAX
}

internal enum SongStream
{
    MainSong,
    EventSong,
};

internal enum StopEffect
{
    AmbientInstance = 4,
}

#pragma warning disable CA1822 // Mark members as static
internal sealed class Sound
{
    public const int AmbientInstance = 4;

    public void PlayEffect(SoundEffect effect) { }
    public void PlayEffect(SoundEffect id, bool loop, int instance) { }
    public void PlaySong(SongId song, SongStream stream, bool loop) { }
    public void PushSong(SongId song) { }
    public void StopEffect(StopEffect effect) { }
    public void StopEffects() { }
    public void Pause() { }
    public void Unpause() { }
    public void StopAll() { }
    public void Update() { }
}
