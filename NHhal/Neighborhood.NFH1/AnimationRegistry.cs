using Neighborhood.Shared.Models;

namespace Neighborhood.NFH1;

/// <summary>
/// Merges generic/anims.xml and level_xxx/anims.xml into a single lookup.
///
/// Priority: level-specific animations override generic ones for the same object.
///
/// Usage:
///   var registry = new AnimationRegistry();
///   registry.Load(genericAnims, levelAnims);
///   var anim = registry.GetAnimation("neighbor", "slip1");
/// </summary>
public class AnimationRegistry
{
    // Key: objectName (lower) -> animName (lower) -> Animation
    private readonly Dictionary<string, Dictionary<string, Animation>> _anims =
        new(StringComparer.OrdinalIgnoreCase);

    // --- Load -----------------------------------------------------------------

    /// <summary>
    /// Loads animations, merging generic and level-specific.
    /// Call with null for either parameter if not available.
    /// </summary>
    public void Load(AnimsFile? generic, AnimsFile? level)
    {
        _anims.Clear();

        if (generic != null) MergeFile(generic);
        if (level   != null) MergeFile(level);   // level overrides generic
    }

    private void MergeFile(AnimsFile file)
    {
        foreach (var obj in file.Objects)
        {
            if (!_anims.TryGetValue(obj.Name, out var animMap))
            {
                animMap = new Dictionary<string, Animation>(StringComparer.OrdinalIgnoreCase);
                _anims[obj.Name] = animMap;
            }

            foreach (var anim in obj.Animations)
                animMap[anim.Name] = anim;
        }
    }

    // --- Lookup ---------------------------------------------------------------

    /// <summary>Returns an animation, or null if not found.</summary>
    public Animation? GetAnimation(string objectName, string animName)
    {
        if (_anims.TryGetValue(objectName, out var animMap) &&
            animMap.TryGetValue(animName, out var anim))
            return anim;
        return null;
    }

    /// <summary>Returns all animation names for an object.</summary>
    public IEnumerable<string> GetAnimationNames(string objectName) =>
        _anims.TryGetValue(objectName, out var map)
            ? map.Keys
            : [];

    /// <summary>True if the object has any registered animations.</summary>
    public bool HasObject(string objectName) =>
        _anims.ContainsKey(objectName);

    /// <summary>All registered object names.</summary>
    public IEnumerable<string> ObjectNames => _anims.Keys;
}
