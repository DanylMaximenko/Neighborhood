using Neighborhood.Shared.Models;

namespace Neighborhood.Shared.Interfaces;

/// <summary>
/// Receives GFX data from the VFS and makes sprites available by name.
/// </summary>
public interface IGfxLoader
{
    /// <summary>
    /// Indexes all .tga entries under "{ns}:gfx:..." in the VFS.
    /// Called by LoaderService after mounting.
    /// </summary>
    void LoadFromVfs(VirtualFileSystem vfs, string ns);
    void SetLevelPriority(string? levelFolder);
    /// <summary>Returns sprite bytes by filename (without path, e.g. "idle_0001.tga").</summary>
    bool TryGetSprite(string spriteName, out byte[] spriteData);
}

/// <summary>
/// Receives SFX data from the VFS and makes audio available by name.
/// </summary>
public interface ISfxLoader
{
    /// <summary>
    /// Indexes all .wav and .mp3 entries under "{ns}:sfx:..." and "{ns}:sfxhi:..." in the VFS.
    /// sfxhi (high quality) entries override sfx entries with the same name.
    /// </summary>
    void LoadFromVfs(VirtualFileSystem vfs, string ns);

    bool TryGetSound(string soundName, out byte[] soundData);
    bool TryGetMusic(string trackName, out byte[] musicData);
}
