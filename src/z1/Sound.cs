using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using z1.IO;

namespace z1;

// HEY! LISTEN!
// I'm not great at audio code. A lot of this could be simplified or made to be better leverage NAudio.
// IE, there's a lot of overlap between the different custom streams here that I believe could be merged.

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
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct SoundInfo
{
    // The loop beginning and end points in frames (1/60 seconds).
    public short Start;
    public short End;
    public sbyte Slot;
    public sbyte Priority;
    public sbyte Flags;
    public sbyte Reserved;
    public fixed byte Filename[20];

    public readonly float StartSeconds => Start * (1 / 60f);
    public readonly float EndSeconds => End * (1 / 60f);

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

    public Asset GetAsset() => new(GetFilename());
    public Stream GetStream() => GetAsset().GetStream();
}

// This is a version of `AudioFileReader` modified to take a stream instead of a filename.
public class StreamedAudioFileReader : WaveStream, ISampleProvider
{
    private WaveStream _readerStream;
    private readonly SampleChannel _sampleChannel;
    private readonly int _destBytesPerSample;
    private readonly int _sourceBytesPerSample;
    private readonly long _length;
    private readonly object _lockObject;

    public StreamedAudioFileReader(Stream stream)
    {
        _lockObject = new object();
        CreateReaderStream(stream);
        _sourceBytesPerSample = _readerStream.WaveFormat.BitsPerSample / 8 * _readerStream.WaveFormat.Channels;
        _sampleChannel = new SampleChannel(_readerStream, false);
        _destBytesPerSample = 4 * _sampleChannel.WaveFormat.Channels;
        _length = SourceToDest(_readerStream.Length);
    }

    private void CreateReaderStream(Stream stream)
    {
        _readerStream = new WaveFileReader(stream);
        if (_readerStream.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat) return;
        _readerStream = WaveFormatConversionStream.CreatePcmStream(_readerStream);
        _readerStream = new BlockAlignReductionStream(_readerStream);
    }

    public override WaveFormat WaveFormat => _sampleChannel.WaveFormat;
    public override long Length => _length;
    public override long Position
    {
        get => SourceToDest(_readerStream.Position);
        set
        {
            lock (_lockObject)
            {
                _readerStream.Position = DestToSource(value);
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var waveBuffer = new WaveBuffer(buffer);
        var count1 = count / 4;
        return Read(waveBuffer.FloatBuffer, offset / 4, count1) * 4;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lockObject)
        {
            return _sampleChannel.Read(buffer, offset, count);
        }
    }

    public float Volume
    {
        get => _sampleChannel.Volume;
        set => _sampleChannel.Volume = value;
    }

    private long SourceToDest(long sourceBytes) => _destBytesPerSample * (sourceBytes / _sourceBytesPerSample);
    private long DestToSource(long destBytes) => _sourceBytesPerSample * (destBytes / _destBytesPerSample);

    protected override void Dispose(bool disposing)
    {
        if (disposing && _readerStream != null)
        {
            _readerStream.Dispose();
            _readerStream = null;
        }
        base.Dispose(disposing);
    }
}

[DebuggerDisplay("{_audioFileName}")]
internal sealed class CachedSound
{
    public float[] AudioData { get; }
    public WaveFormat WaveFormat { get; }

    private readonly string _audioFileName; // for debug.

    public CachedSound(Asset asset)
    {
        _audioFileName = asset.Filename;
        using var audioFileReader = new StreamedAudioFileReader(asset.GetStream());

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

    public TimeSpan GetLength()
    {
        var totalSamples = AudioData.Length / WaveFormat.Channels;
        var seconds = (float)totalSamples / WaveFormat.SampleRate;
        return TimeSpan.FromSeconds(seconds);
    }
}

internal sealed class CachedSampleProvider : ISampleProvider
{
    public bool HasReachedEnd { get; private set; }
    public bool Loop { get; set; }

    public WaveFormat WaveFormat => _cachedSound.WaveFormat;
    private readonly CachedSound _cachedSound;
    private int _position;

    public CachedSampleProvider(CachedSound cachedSound, bool loop)
    {
        _cachedSound = cachedSound;
        Loop = loop;
    }

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
                if (!Loop)
                {
                    HasReachedEnd = true;
                    break;
                }

                _position = 0;
            }
        }

        return totalSamplesCopied;
    }
}

[DebuggerDisplay("{Name}")]
internal sealed class LoopStream : WaveStream
{
    public string Name { get; }
    public bool HasReachedEnd { get; private set; }
    public ISampleProvider SampleProvider { get; }
    private long _pausedSpot = 0;

    private readonly WaveFileReader _sourceStream;
    private readonly long _startPosition;
    private readonly long _endPosition;

    public LoopStream(NamedWaveFileReader sourceStream, bool loop, float? startTimeInSeconds, float? endTimeInSeconds)
    {
        Name = sourceStream.Name;
        _sourceStream = sourceStream;
        var averageBytesPerSecond = sourceStream.WaveFileReader.WaveFormat.AverageBytesPerSecond;
        var sourceLength = sourceStream.WaveFileReader.Length;

        _startPosition = startTimeInSeconds.HasValue ? (long)(startTimeInSeconds.Value * averageBytesPerSecond) : 0;
        _endPosition = endTimeInSeconds.HasValue ? (long)(endTimeInSeconds.Value * averageBytesPerSecond) : sourceLength;

        if (_endPosition > sourceLength)
        {
            _endPosition = sourceLength;
        }

        sourceStream.WaveFileReader.Position = _startPosition;
        EnableLooping = loop;
        SampleProvider = this.ToSampleProvider();
    }

    public void Pause() => _pausedSpot = _sourceStream.Position;
    public void Unpause() => _sourceStream.Position = _pausedSpot;

    public bool EnableLooping { get; set; }
    public override WaveFormat WaveFormat => _sourceStream.WaveFormat;
    public override long Length => _sourceStream.Length;

    public override long Position
    {
        get => _sourceStream.Position - _startPosition;
        set
        {
            var newPosition = _startPosition + value;
            if (newPosition < _startPosition || newPosition > _endPosition)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Position must be within the looped section.");
            }
            _sourceStream.Position = newPosition;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalBytesRead = 0;
        while (totalBytesRead < count)
        {
            var bytesRequired = count - totalBytesRead;
            var bytesAvailable = (int)(_endPosition - _sourceStream.Position);
            var bytesToRead = Math.Min(bytesRequired, bytesAvailable);

            var bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, bytesToRead);
            if (bytesRead == 0)
            {
                if (_sourceStream.Position == 0 || !EnableLooping)
                {
                    HasReachedEnd = true;
                    break;
                }

                _sourceStream.Position = _startPosition;
            }

            totalBytesRead += bytesRead;

            if (_sourceStream.Position >= _endPosition)
            {
                if (!EnableLooping)
                {
                    HasReachedEnd = true;
                    break;
                }

                _sourceStream.Position = _startPosition;
            }
        }
        return totalBytesRead;
    }
}

// This is to make debugging easier.
[DebuggerDisplay("{Name}")]
internal sealed class NamedWaveFileReader
{
    public string Name { get; }
    public WaveFileReader WaveFileReader { get; }

    public NamedWaveFileReader(Asset asset)
    {
        Name = asset.Filename;
        WaveFileReader = new WaveFileReader(asset.GetStream());
    }

    public static implicit operator WaveFileReader(NamedWaveFileReader named) => named.WaveFileReader;
}

internal sealed class Sound
{
    public const int AmbientInstance = 4;

    private const int Instances = 5;
    private const int Streams = (int)SongStream.MAX;
    private const int LoPriStreams = Streams - 1;
    private const int Songs = (int)SongId.MAX;
    private const int Effects = (int)SoundEffect.MAX;
    private const int VolumeIncrements = 5;

    private readonly record struct EffectRequest(SoundEffect SoundId, bool Loop);

    private static readonly DebugLog _traceLog = new(nameof(Sound), DebugLogDestination.DebugBuildsOnly);

    private readonly CachedSound[] _effectSamples = new CachedSound[Effects];
    private readonly SoundInfo[] _effects;
    private readonly SoundInfo[] _songs;
    private readonly NamedWaveFileReader[] _songFiles = new NamedWaveFileReader[Songs];
    private readonly EffectRequest?[] _effectRequests = new EffectRequest?[Instances];
    private readonly WaveOutEvent _waveOutDevice;
    private readonly MixingSampleProvider _mixer;

    private readonly LoopStream?[] _playingSongSamples = new LoopStream[Streams];
    private readonly LoopStream?[] _savedPlayingSongSamples = new LoopStream[LoPriStreams];
    private readonly CachedSampleProvider?[] _playingEffectSamples = new CachedSampleProvider[Instances];

    private int _volume; // 0 to 100.
    private bool _isMuted = false;

    /// <param name="volume">0 to 100</param>
    public Sound(int volume)
    {
        _waveOutDevice = new WaveOutEvent
        {
            DesiredLatency = 50,
            NumberOfBuffers = 2,
        };
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
        {
            ReadFully = true,
        };
        _waveOutDevice.Init(_mixer);
        _waveOutDevice.Play();

        _effects = ListResource<SoundInfo>.LoadList("Effects.dat", Effects).ToArray();
        for (var i = 0; i < Effects; i++)
        {
            _effectSamples[i] = new CachedSound(_effects[i].GetAsset());
        }

        _songs = ListResource<SoundInfo>.LoadList("Songs.dat", Songs).ToArray();
        for (var i = 0; i < Songs; i++)
        {
            _songFiles[i] = new NamedWaveFileReader(_songs[i].GetAsset());
        }

        SetVolume(volume);
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        _waveOutDevice.Volume = _volume / 100f;
    }

    public int IncreaseVolume()
    {
        _volume = Math.Min(_volume + VolumeIncrements, 100);
        _waveOutDevice.Volume = _volume / 100f;
        return _volume;
    }

    public int DecreaseVolume()
    {
        _volume = Math.Max(_volume - VolumeIncrements, 0);
        _waveOutDevice.Volume = _volume / 100f;
        return _volume;
    }

    public bool ToggleMute()
    {
        _waveOutDevice.Volume = _isMuted ? _volume / 100f : 0;
        return _isMuted = !_isMuted;
    }

    private void PlaySongInternal(SongId song, SongStream stream, bool loop, bool play)
    {
        _traceLog.Write($"PlaySongInternal({song}, {stream}, {loop}, {play})");
        ref var streamBucket = ref StopSong((int)stream);
        var songinfo = _songs[(int)song];
        // JOE: TODO: Cleanup this line.
        var waveStream = new LoopStream(_songFiles[(int)song], loop, songinfo.StartSeconds, songinfo.EndSeconds);
        streamBucket = waveStream;
        _mixer.AddMixerInput(streamBucket.SampleProvider);
    }

    public void PlayEffect(SoundEffect id, bool loop = false, int instance = -1)
    {
        _traceLog.Write($"PlayEffect({id}, {loop}, {instance})");
        if (id < 0 || (int)id >= _effectSamples.Length) throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown SoundEffect");
        if (instance is < -1 or >= Instances) throw new ArgumentOutOfRangeException(nameof(instance), instance, "Unknown instance");

        var index = instance == -1 ? _effects[(int)id].Slot - 1 : instance;

        ref var request = ref _effectRequests[index];
        if (request == null || _effects[(int)id].Priority < _effects[(int)request.Value.SoundId].Priority)
        {
            request = new EffectRequest(id, loop);
            return;
        }

        _traceLog.Write($"PlayEffect({id}, {loop}, {instance}) will not play.");
    }

    private void UpdateSongs()
    {
        // JOE: TODO:
        // if (_paused)
        //     return;

        ref var eventSong = ref _playingSongSamples[(int)SongStream.EventSong];
        if (eventSong == null || !eventSong.HasReachedEnd)
        {
            return;
        }

        eventSong.Dispose();
        eventSong = null;

        _traceLog.Write($"UpdateSongs() Checking LoPriStreams");

        // JOE: This is notably different from the C++.
        for (var i = 0; i < LoPriStreams; i++)
        {
            ref var saved = ref _savedPlayingSongSamples[i];
            if (saved != null)
            {
                _traceLog.Write($"UpdateSongs(), resuming saved={saved.Name}");
                saved.Unpause();
                _mixer.AddMixerInput(saved.SampleProvider);
                _playingSongSamples[i] = saved;
            }
        }
    }

    private void UpdateEffects()
    {
        for (var i = 0; i < Instances; i++)
        {
            ref var request = ref _effectRequests[i];
            if (request == null) continue;

            var soundId = request.Value.SoundId;
            var loop = request.Value.Loop;
            request = null;

            ref var instance = ref _playingEffectSamples[i];

            var hasPlayIfQuietSlot = _effects[(int)soundId].HasPlayIfQuietSlot;
            _traceLog.Write($"UpdateEffects({soundId}, {loop}), hasPlayIfQuietSlot={hasPlayIfQuietSlot}, instance={instance?.HasReachedEnd}");
            if (!hasPlayIfQuietSlot || (instance == null || instance.HasReachedEnd))
            {
                if (instance != null) _mixer.RemoveMixerInput(instance);

                var sample = _effectSamples[(int)soundId];
                _traceLog.Write($"UpdateEffects({soundId}, {loop}), playing in i={i} ({sample.GetLength()})");
                instance = new CachedSampleProvider(sample, loop);
                _mixer.AddMixerInput(instance);
            }
        }
    }

    public void PlaySong(SongId song, SongStream stream, bool loop)
    {
        _traceLog.Write($"PlaySong({song}, {stream}, {loop})");
        if (stream < 0 || (int)stream >= LoPriStreams) return;
        if (song < 0 || (int)song >= _songs.Length) return;

        var eventSong = _playingSongSamples[(int)SongStream.EventSong];
        if (eventSong == null || eventSong.HasReachedEnd)
        {
            PlaySongInternal(song, stream, loop, true);
            return;
        }

        PlaySongInternal(song, stream, loop, false);
        _savedPlayingSongSamples[(int)stream] = null;
    }

    private ref LoopStream? StopSong(int i, bool dispose = false)
    {
        ref var streamBucket = ref _playingSongSamples[i];
        if (streamBucket != null)
        {
            _mixer.RemoveMixerInput(streamBucket.SampleProvider);
            // JOE: TODO: Dispose here I believe?
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

    public void PushSong(SongId song)
    {
        _traceLog.Write($"PushSong({song})");

         for (var i = 0; i < LoPriStreams; i++)
         {
             var stream = _playingSongSamples[i];
             if (stream != null && !stream.HasReachedEnd)
             {
                 _traceLog.Write($"PushSong({song}), saving {stream.Name}");
                stream.Pause();
                StopSong(i, false);
                ref var savedSlot = ref _savedPlayingSongSamples[i];
                savedSlot?.Dispose();
                savedSlot = stream;
             }
         }

         PlaySongInternal(song, SongStream.EventSong, false, true);
    }

    public void StopEffect(StopEffect effect) => StopEffect((int)effect);
    public void StopEffect(int effectId)
    {
        _traceLog.Write($"StopEffect({effectId})");
        ref var effect = ref _playingEffectSamples[effectId];
        if (effect != null)
        {
            _mixer.RemoveMixerInput(effect);
            effect.Loop = false;
            effect = null;
        }
    }

    public void StopEffects()
    {
        _traceLog.Write("StopEffects()");
        for (var i = 0; i < Instances; i++)
        {
            StopEffect(i);
        }
    }

    public void Pause()
    {
        _traceLog.Write("Pause()");
        _waveOutDevice.Pause();
    }

    public void Unpause()
    {
        _traceLog.Write("Unpause()");
        _waveOutDevice.Play();
    }

    public void StopAll()
    {
        _traceLog.Write("StopAll()");
        // _waveOutDevice.Stop();
        StopSongs();
        StopEffects();
    }

    public void Update()
    {
        UpdateSongs();
        UpdateEffects();
    }
}
