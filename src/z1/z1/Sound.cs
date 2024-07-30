using System.Buffers.Binary;
using System.Text;
using Silk.NET.OpenAL;

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
}

internal enum StopEffect
{
    AmbientInstance = 4,
}

internal unsafe struct SoundInfo
{
    // The loop beginning and end points in frames (1/60 seconds).
    public short Begin;
    public short End;
    public sbyte Slot;
    public sbyte Priority;
    public sbyte Flags;
    public sbyte Reserved;
    public fixed byte Filename[20];

    public string GetFilename()
    {
        fixed (byte* p = Filename)
        {
            var span = new ReadOnlySpan<byte>(p, 20);
            var length = span.IndexOf((byte)0);
            if (length == -1) length = 20;
            return Encoding.ASCII.GetString(p, length);
        }
    }
}

internal sealed unsafe class WaveFile
{
    // Largely based on the sample source: https://github.com/dotnet/Silk.NET/blob/main/examples/CSharp/OpenAL%20Demos/WavePlayer/Program.cs

    private readonly byte[] _bytes;
    private readonly short _numChannels;
    private readonly int _sampleRate;
    private readonly int _byteRate;
    private readonly short _blockAlign;
    private readonly short _bitsPerSample;
    private readonly BufferFormat _format;

    public WaveFile(string path)
    {
        _bytes = Assets.ReadAllBytes(path);
        ReadOnlySpan<byte> span = _bytes.AsSpan();

        var riffSignature = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (riffSignature != 0x46464952) throw new Exception($"Invalid wave {path}. Got riffSignature: {riffSignature:X8}");
        span = span[4..];

        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(span);
        span = span[4..];

        var waveSignature = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (waveSignature != 0x45564157) throw new Exception($"Invalid wave {path}. Got waveSignature: {waveSignature:X8}");
        span = span[4..];

        // Trim off header because it's no longer needed.
        _bytes = span.ToArray();

        while (span.Length > 0)
        {
            var identifier = Encoding.UTF8.GetString(span[..4]);
            span = span[4..];
            var size = BinaryPrimitives.ReadInt32LittleEndian(span);
            span = span[4..];
            switch (identifier)
            {
                case "fmt ":
                    if (size != 16) throw new Exception($"Unknown Audio Format with subchunk1 size {size}");
                    var audioFormat = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                    span = span[2..];
                    if (audioFormat != 1) throw new Exception($"Unknown Audio Format with ID {audioFormat}");

                    _numChannels = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                    span = span[2..];
                    _sampleRate = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                    span = span[4..];
                    _byteRate = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                    span = span[4..];
                    _blockAlign = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                    span = span[2..];
                    _bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                    span = span[2..];

                    _format = (numChannels: _numChannels, bitsPerSample: _bitsPerSample) switch
                    {
                        (1, 8) => BufferFormat.Mono8,
                        (1, 16) => BufferFormat.Mono16,
                        (2, 8) => BufferFormat.Stereo8,
                        (2, 16) => BufferFormat.Stereo16,
                        _ => throw new Exception($"Can't Play {_bitsPerSample}bits for {_numChannels} channels sound.")
                    };
                    return;
                default:
                    span = span[size..];
                    break;
            }
        }
    }

    public void Play(AL al)
    {
        ReadOnlySpan<byte> span = _bytes.AsSpan();
        var source = al.GenSource();
        var buffer = al.GenBuffer();

        while (span.Length > 0)
        {
            var identifier = Encoding.ASCII.GetString(span[..4]);
            span = span[4..];
            var size = BinaryPrimitives.ReadInt32LittleEndian(span);
            span = span[4..];
            switch (identifier)
            {
                case "data":
                    var data = span[..size];
                    span = span[size..];

                    fixed (byte* pData = data)
                    {
                        al.BufferData(buffer, _format, pData, size, _sampleRate);
                    }
                    Console.WriteLine($"Read {size} bytes Data");
                    break;
                default:
                    span = span[size..];
                    break;
            }
        }

        al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
        al.SourcePlay(source);

        // al.DeleteSource(source);
        // al.DeleteBuffer(buffer);
    }
}

#pragma warning disable CA1822 // Mark members as static
internal sealed unsafe class Sound
{
    private const int Instances = 5;
    private const int Streams = 2;
    private const int LoPriStreams = Streams - 1;
    private const int Songs = (int)SongId.MAX;
    private const int Effects = (int)SoundEffect.MAX;
    private const int NoSound = 0xFF;
    public const int AmbientInstance = 4;

    private bool _disableAudio = false;

    private WaveFile[] effectSamples = new WaveFile[Effects];
    private SoundInfo[] effects;

    private readonly AL _al;

    public Sound()
    {
        ALContext alc;
        try
        {
            alc = ALContext.GetApi();
        }
        catch (FileNotFoundException)
        {
            // JOE: TODO: Report this back to the user. They need to install OpenAL.
            _disableAudio = true;
            return;
        }

        _al = AL.GetApi();
        var device = alc.OpenDevice("");
        if (device == null)
        {
            Console.WriteLine("Could not create device");
            return;
        }

        var context = alc.CreateContext(device, null);
        alc.MakeContextCurrent(context);
        _al.GetError();

        effects = ListResource<SoundInfo>.LoadList("Effects.dat", Effects).ToArray();
        for (var i = 0; i < Effects; i++)
        {
            effectSamples[i] = new WaveFile(effects[i].GetFilename());
        }
    }

    public void PlayEffect(SoundEffect id, bool loop = false, int instance = -1)
    {
        if (id < 0 || (int)id >= effectSamples.Length) return;
        if (instance is < -1 or >= Instances) return;

        int index;

        if (instance == -1)
            index = effects[(int)id].Slot - 1;
        else
            index = instance;

        effectSamples[index].Play(_al);

        // int prevId = effectRequests[index].SoundId;
        //
        // if ((prevId == NoSound) || (effects[id].Priority < effects[prevId].Priority))
        // {
        //     effectRequests[index].SoundId = id;
        //     effectRequests[index].Loop = loop;
        // }
    }
    public void PlaySong(SongId song, SongStream stream, bool loop) { }
    public void PushSong(SongId song) { }
    public void StopEffect(StopEffect effect) { }
    public void StopEffects() { }
    public void Pause() { }
    public void Unpause() { }
    public void StopAll() { }
    public void Update() { }
}
