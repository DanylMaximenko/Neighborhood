using System.Runtime.InteropServices;
using Neighborhood.Shared.Interfaces;
using Neighborhood.Shared.Models;

namespace Neighborhood.SFXLoader;

/// <summary>
/// Implements ISfxLoader -- indexes audio from VFS and plays it on demand.
///
/// Audio formats in NFH archives:
///   .wav -- sound effects (short, fire-and-forget)
///   .mp3 -- background music (looped, one track at a time)
///
/// Playback:
///   WAV  -> System.Media.SoundPlayer via MemoryStream (zero deps, low latency)
///   MP3  -> Windows MCI (mciSendString) via temp file
///
/// No NuGet dependencies -- works on any Windows .NET 8 install.
/// Editor.exe reuses AudioPlayer directly for sound preview.
/// </summary>
public sealed class SfxLoader : ISfxLoader, IDisposable
{
    private readonly Dictionary<string, byte[]> _sounds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _music =
        new(StringComparer.OrdinalIgnoreCase);

    private AudioPlayer? _player;

    public AudioPlayer? Player => _player;

    public void LoadFromVfs(VirtualFileSystem vfs, string ns)
    {
        _sounds.Clear();
        _music.Clear();
        _player?.Dispose();

        IndexPrefix(vfs, $"{ns}:sfx:");
        IndexPrefix(vfs, $"{ns}:sfxhi:"); // high quality overwrites standard

        _player = new AudioPlayer(_sounds, _music);
    }

    public bool TryGetSound(string soundName, out byte[] soundData) =>
        _sounds.TryGetValue(soundName, out soundData!);

    public bool TryGetMusic(string trackName, out byte[] musicData) =>
        _music.TryGetValue(trackName, out musicData!);

    private void IndexPrefix(VirtualFileSystem vfs, string prefix)
    {
        foreach (var key in vfs.ListKeys(prefix))
        {
            if (!vfs.TryGet(key, out var entry)) continue;
            if (entry.Extension == ".wav")      _sounds[entry.FileName] = entry.Data;
            else if (entry.Extension == ".mp3") _music[entry.FileName]  = entry.Data;
        }
    }

    public void Dispose() => _player?.Dispose();
}

/// <summary>
/// Handles actual audio playback. Separated from SfxLoader so
/// Editor.exe can use it independently for sound preview.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    private readonly IReadOnlyDictionary<string, byte[]> _sounds;
    private readonly IReadOnlyDictionary<string, byte[]> _music;

    private string? _currentMusicAlias;
    private string? _currentTempFile;
    private bool    _disposed;

    public AudioPlayer(
        IReadOnlyDictionary<string, byte[]> sounds,
        IReadOnlyDictionary<string, byte[]> music)
    {
        _sounds = sounds;
        _music  = music;
    }

    // --- WAV -----------------------------------------------------------------

    /// <summary>Plays a WAV sound effect asynchronously (fire-and-forget).</summary>
    public void PlaySound(string soundName)
    {
        if (!_sounds.TryGetValue(soundName, out var data)) return;
        Task.Run(() =>
        {
            try
            {
                using var ms     = new MemoryStream(data);
                using var player = new System.Media.SoundPlayer(ms);
                player.PlaySync();
            }
            catch { }
        });
    }

    // --- MP3 (Windows MCI) ---------------------------------------------------

    /// <summary>Starts an MP3 music track, stopping any currently playing track.</summary>
    public void PlayMusic(string trackName, bool loop = true)
    {
        if (!_music.TryGetValue(trackName, out var data)) return;

        StopMusic();

        var tempPath = Path.Combine(Path.GetTempPath(), $"nfh_{Guid.NewGuid():N}.mp3");
        File.WriteAllBytes(tempPath, data);

        var alias = $"nfh_{Environment.TickCount64}";
        Mci($"open \"{tempPath}\" type mpegvideo alias {alias}");
        Mci($"play {alias}{(loop ? " repeat" : "")}");

        _currentMusicAlias = alias;
        _currentTempFile   = tempPath;
    }

    public void StopMusic()
    {
        if (_currentMusicAlias != null)
        {
            Mci($"stop {_currentMusicAlias}");
            Mci($"close {_currentMusicAlias}");
            _currentMusicAlias = null;
        }
        if (_currentTempFile != null)
        {
            try { File.Delete(_currentTempFile); } catch { }
            _currentTempFile = null;
        }
    }

    public void PauseMusic()
    {
        if (_currentMusicAlias != null) Mci($"pause {_currentMusicAlias}");
    }

    public void ResumeMusic()
    {
        if (_currentMusicAlias != null) Mci($"resume {_currentMusicAlias}");
    }

    public bool IsMusicPlaying => _currentMusicAlias != null;

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(
        string command, string? ret, int retLen, IntPtr hwnd);

    private static void Mci(string cmd) =>
        mciSendString(cmd, null, 0, IntPtr.Zero);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMusic();
    }
}
