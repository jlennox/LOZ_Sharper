using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
    MAX,
}

internal enum StopEffect
{
    AmbientInstance = 4,
}

[Flags]
internal enum SoundFlags
{
    None = 0,
    PlayIfQuietSlot = 1,
};

[StructLayout(LayoutKind.Sequential, Pack = 1)]
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

    public readonly bool HasPlayIfQuietSlot => (Flags & (byte)SoundFlags.PlayIfQuietSlot) != 0;

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

[DebuggerDisplay("{_audioFileName}")]
internal class CachedSound
{
    public float[] AudioData { get; }
    public WaveFormat WaveFormat { get; }

    private readonly string _audioFileName; // for debug.

    public CachedSound(string audioFileName)
    {
        _audioFileName = audioFileName;
        using var audioFileReader = new AudioFileReader(audioFileName);
        WaveFormat = audioFileReader.WaveFormat;
        var wholeFile = new List<float>((int)(audioFileReader.Length / 4));
        var readBuffer = new float[audioFileReader.WaveFormat.SampleRate * audioFileReader.WaveFormat.Channels];
        int samplesRead;
        while ((samplesRead = audioFileReader.Read(readBuffer, 0, readBuffer.Length)) > 0)
        {
            wholeFile.AddRange(readBuffer.AsSpan()[..samplesRead]);
        }
        AudioData = wholeFile.ToArray();
    }
}

internal class CachedSoundSampleProvider : ISampleProvider
{
    private readonly CachedSound _cachedSound;
    private readonly bool _loop;
    private int _position;

    public CachedSoundSampleProvider(CachedSound cachedSound, bool loop)
    {
        _cachedSound = cachedSound;
        _loop = loop;
    }

    public WaveFormat WaveFormat => _cachedSound.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var totalSamplesCopied = 0;

        while (totalSamplesCopied < count)
        {
            var availableSamples = _cachedSound.AudioData.Length - _position;
            var samplesToCopy = Math.Min(availableSamples, count - totalSamplesCopied);
            Array.Copy(_cachedSound.AudioData, _position, buffer, offset + totalSamplesCopied, samplesToCopy);
            totalSamplesCopied += samplesToCopy;
            _position += samplesToCopy;

            if (_position >= _cachedSound.AudioData.Length)
            {
                if (!_loop) break;
                _position = 0;
            }
        }

        return totalSamplesCopied;
    }
}

internal sealed record EffectRequest(SoundEffect SoundId, bool Loop);

internal sealed class LoopStream : WaveStream
{
    private readonly WaveStream _sourceStream;

    public LoopStream(WaveStream sourceStream, bool loop)
    {
        _sourceStream = sourceStream;
        EnableLooping = loop;
    }

    public bool EnableLooping { get; set; }
    public override WaveFormat WaveFormat => _sourceStream.WaveFormat;
    public override long Length => _sourceStream.Length;
    public override long Position
    {
        get => _sourceStream.Position;
        set => _sourceStream.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < count)
        {
            var bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
            if (bytesRead == 0)
            {
                if (_sourceStream.Position == 0 || !EnableLooping) break;
                _sourceStream.Position = 0;
            }
            totalBytesRead += bytesRead;
        }
        return totalBytesRead;
    }
}

#pragma warning disable CA1822 // Mark members as static
internal sealed class Sound
{
    private const int Instances = 5;
    private const int Streams = (int)SongStream.MAX;
    private const int LoPriStreams = Streams - 1;
    private const int Songs = (int)SongId.MAX;
    private const int Effects = (int)SoundEffect.MAX;
    private const int NoSound = 0xFF;
    public const int AmbientInstance = 4;

    private readonly bool _disableAudio = false;

    private CachedSound[] effectSamples = new CachedSound[Effects];
    private SoundInfo[] effects;
    static double[] savedPos = new double[LoPriStreams];
    private SoundInfo[] songs;
    private AudioFileReader[] songFiles = new AudioFileReader[Songs];
    private readonly EffectRequest?[] effectRequests = new EffectRequest[Instances];
    private WaveOutEvent _waveOutDevice;
    private readonly MixingSampleProvider _mixer;

    private readonly ISampleProvider?[] _playingSongSamples = new ISampleProvider[Streams];

    public Sound()
    {
        _waveOutDevice = new WaveOutEvent();
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
        {
            ReadFully = true
        };
        _waveOutDevice.Init(_mixer);
        _waveOutDevice.Play();

        effects = ListResource<SoundInfo>.LoadList("Effects.dat", Effects).ToArray();
        for (var i = 0; i < Effects; i++)
        {
            effectSamples[i] = new CachedSound(Asset.GetPath(effects[i].GetFilename()));
        }

        songs = ListResource<SoundInfo>.LoadList("Songs.dat", Songs).ToArray();
        for (var i = 0; i < Songs; i++)
        {
            songFiles[i] = new AudioFileReader(Asset.GetPath(songs[i].GetFilename()));
        }
    }

    private void PlaySongInternal(SongId song, SongStream stream, bool loop, bool play)
    {
        ref var streamBucket = ref StopSong((int)stream);
        var waveStream = new LoopStream(songFiles[(int)song], loop);
        streamBucket = waveStream.ToSampleProvider();
        _mixer.AddMixerInput(streamBucket);
    }

    public void PlayEffect(SoundEffect id, bool loop = false, int instance = -1)
    {
        if (id < 0 || (int)id >= effectSamples.Length) throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown SoundEffect");
        if (instance is < -1 or >= Instances) throw new ArgumentOutOfRangeException(nameof(instance), instance, "Unknown instance");

        var index = instance == -1 ? effects[(int)id].Slot - 1 : instance;

        ref var request = ref effectRequests[index];
        if (request == null || effects[(int)id].Priority < effects[(int)request.SoundId].Priority)
        {
            request = new EffectRequest(id, loop);
        }
    }

    private void UpdateEffects()
    {
        for (var i = 0; i < Instances; i++)
        {
            ref var request = ref effectRequests[i];
            if (request == null) continue;
            var id = request.SoundId;
            // JOE: TODO: Arg. Need to support this.
            // if (!effects[(int)id].HasPlayIfQuietSlot)
            {
                var sample = effectSamples[(int)id];
                var input = new CachedSoundSampleProvider(sample, request.Loop);
                _mixer.AddMixerInput(input);
            }

            request = null;
        }
    }

    public void PlaySong(SongId song, SongStream stream, bool loop)
    {
        if (stream < 0 || (int)stream >= LoPriStreams)return;
        if (song < 0 || (int)song >= songs.Length)return;

        // if (songStreams[(int)SongStream.EventSong].PlaybackState == PlaybackState.Stopped)
        {
            PlaySongInternal(song, stream, loop, true);
            return;
        }

        PlaySongInternal(song, stream, loop, false);
        savedPos[(int)stream] = 0;
    }

    private ref ISampleProvider? StopSong(int i)
    {
        ref var streamBucket = ref _playingSongSamples[i];
        if (streamBucket != null)
        {
            _mixer.RemoveMixerInput(streamBucket);
            streamBucket = null;
        }
        return ref streamBucket;
    }

    private void StopSongs()
    {
        for (var i = 0; i < Streams; i++)
        {
            StopSong(i);
        }
    }

    public void PushSong(SongId song) { }

    public void StopEffect(StopEffect effect) { }
    public void StopEffects() { }
    public void Pause() => _waveOutDevice.Pause();
    public void Unpause() => _waveOutDevice.Play();
    public void StopAll()
    {
        _waveOutDevice.Stop();
        // StopSongs();
        // StopEffects();
    }
    public void Update()
    {
        if (_waveOutDevice.PlaybackState == PlaybackState.Stopped)
        {
            _waveOutDevice.Play();
        }
        UpdateEffects();
    }
}
