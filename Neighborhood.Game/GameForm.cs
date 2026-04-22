using System.Diagnostics;
using System.Drawing.Drawing2D;
using Neighborhood.GFXLoader;
using Neighborhood.Loader;
using Neighborhood.NFH1;
using Neighborhood.Shared.Models;
using Neighborhood.SFXLoader;

namespace Neighborhood.Game;

/// <summary>
/// Rendering resolution mode.
/// Original: 800x600 -- matches the original game exactly, 1:1 world-to-pixel.
/// HD: 1280x720 -- uniform 1.2x scale (min(1280/800, 720/600)), wider viewport.
/// </summary>
public enum RenderMode { Original, HD }

internal sealed class GameForm : Form
{
    // Original game resolution
    public const int OriginalWidth  = 800;
    public const int OriginalHeight = 600;

    // HD resolution
    public const int HdWidth  = 1280;
    public const int HdHeight = 720;

    // Uniform scale factor for HD: min(1280/800, 720/600) = 1.2
    public const float HdScale = 1.2f;

    private readonly GameLogic _logic;
    private readonly LoaderService _loader;
    private readonly GfxLoader _gfx;
    private readonly SfxLoader _sfx;
    private readonly GameVariant _variant;
    private readonly string _levelName;
    private readonly RenderMode _renderMode;
    private readonly BndLoader _parser = new();

    // Active scale factor: 1.0 for Original, HdScale for HD
    private float Scale => _renderMode == RenderMode.HD ? HdScale : 1.0f;

    private readonly Stopwatch _clock = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };

    private readonly Dictionary<string, string> _actorSprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _objectSprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, NfhPoint>> _ownerSpriteOffsets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missingSpriteWarnings = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _objectRooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Rectangle> _objectHitAreas = new(StringComparer.OrdinalIgnoreCase);

    private string _statusText = "LMB: move / interact, RMB: force alert test, Arrows: scroll camera";
    private int _statusUntilMs;
    private readonly HashSet<Keys> _heldKeys = new();

    // Free camera - moves with arrow keys or mouse edge scrolling
    private float _camX;
    private float _camY;
    private bool  _camInitialized;

    // Camera scroll speed in world units per second
    private const float CamScrollSpeed = 400f;
    // Mouse edge scroll zone width in pixels
    private const int   EdgeScrollZone = 40;

    public GameForm(
        GameLogic logic,
        LoaderService loader,
        GfxLoader gfx,
        SfxLoader sfx,
        GameVariant variant,
        string levelName,
        RenderMode renderMode = RenderMode.HD)
    {
        _logic = logic;
        _loader = loader;
        _gfx = gfx;
        _sfx = sfx;
        _variant = variant;
        _levelName = levelName;
        _renderMode = renderMode;

        var (w, h) = renderMode == RenderMode.Original
            ? (OriginalWidth, OriginalHeight)
            : (HdWidth, HdHeight);

        Text = $"Neighborhood - {variant} - {levelName} [{renderMode}]";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(w, h);
        MinimumSize = new Size(w, h);
        MaximumSize = new Size(w, h);  // fixed size -- no resize in original mode
        DoubleBuffered = true;

        _timer.Tick += OnTick;
        MouseDown   += OnMouseDown;
        Paint       += OnPaint;
        KeyDown     += (s, e) => _heldKeys.Add(e.KeyCode);
        KeyUp       += (s, e) => _heldKeys.Remove(e.KeyCode);
        KeyPreview   = true;  // receive key events even when child controls have focus

        InitialiseRuntime();
    }

    private void InitialiseRuntime()
    {
        var world = _logic.World;
        if (world == null)
            return;

        // Set level-priority so sprites from the active level folder are preferred
        // over identically-named sprites from other levels in gfxdata.bnd.
        // e.g. ship1/ms_0000.tga wins over cn_b1/ms_0000.tga when ship1 is active.
        _gfx.SetLevelPriority(_levelName);

        BuildObjectRoomMap(world.Layout);
        LoadGfxOffsets();
        InitialiseAnimationSystem();

        _clock.Start();
        _timer.Start();
    }

    private void InitialiseAnimationSystem()
    {
        var anim = _logic.Animations;
        if (anim == null)
            return;

        var ns = _variant == GameVariant.NFH2 ? "nfh2" : "nfh1";

        var genericAnims = TryParseAnims(ns, "generic");
        var levelAnims = TryParseAnims(ns, _levelName);
        var genericObjects = TryParseObjects(ns, "generic") ?? ObjectsFile.Empty;
        var levelObjects = TryParseObjects(ns, _levelName) ?? ObjectsFile.Empty;

        anim.Initialise(genericAnims, levelAnims, genericObjects, levelObjects);
        anim.ActorSpriteChanged += OnActorSpriteChanged;
        anim.ObjectSpriteChanged += OnObjectSpriteChanged;
        anim.SoundTriggered += OnSoundTriggered;

        SyncSpriteStateFromAnimators();

        // Kick one update so the first visible frames are available immediately.
        _logic.Update(1);
    }

    private void SyncSpriteStateFromAnimators()
    {
        var world = _logic.World;
        var anim = _logic.Animations;
        if (world == null || anim == null)
            return;

        foreach (var actor in EnumerateActors(world))
        {
            var animator = anim.GetActorAnimator(actor.Name);
            if (animator != null && !string.IsNullOrEmpty(animator.CurrentSpriteName))
                _actorSprites[actor.Name] = animator.CurrentSpriteName;
        }

        foreach (var obj in world.Objects.Values)
        {
            var animator = anim.GetObjectAnimator(obj.Name);
            if (animator != null && !string.IsNullOrEmpty(animator.CurrentSpriteName))
                _objectSprites[obj.Name] = animator.CurrentSpriteName;
        }
    }

    private string? ResolveActorSprite(string actorName)
    {
        if (_actorSprites.TryGetValue(actorName, out var sprite) && !string.IsNullOrEmpty(sprite))
            return sprite;

        var animator = _logic.Animations?.GetActorAnimator(actorName);
        if (animator != null && !string.IsNullOrEmpty(animator.CurrentSpriteName))
        {
            _actorSprites[actorName] = animator.CurrentSpriteName;
            return animator.CurrentSpriteName;
        }

        return null;
    }

    private string? ResolveObjectSprite(string objectName)
    {
        if (_objectSprites.TryGetValue(objectName, out var sprite) && !string.IsNullOrEmpty(sprite))
            return sprite;

        var animator = _logic.Animations?.GetObjectAnimator(objectName);
        if (animator != null && !string.IsNullOrEmpty(animator.CurrentSpriteName))
        {
            _objectSprites[objectName] = animator.CurrentSpriteName;
            return animator.CurrentSpriteName;
        }

        return null;
    }

    private void LoadGfxOffsets()
    {
        var ns = _variant == GameVariant.NFH2 ? "nfh2" : "nfh1";

        var generic = TryParseGfxData(ns, "generic");
        var level = TryParseGfxData(ns, _levelName);

        if (generic != null) MergeGfxOffsets(generic);
        if (level != null) MergeGfxOffsets(level);
    }

    private void MergeGfxOffsets(GfxDataFile file)
    {
        foreach (var owner in file.Objects)
        {
            if (!_ownerSpriteOffsets.TryGetValue(owner.Name, out var map))
            {
                map = new Dictionary<string, NfhPoint>(StringComparer.OrdinalIgnoreCase);
                _ownerSpriteOffsets[owner.Name] = map;
            }

            foreach (var f in owner.Files)
                map[f.Image] = f.Offset;
        }
    }

    private AnimsFile? TryParseAnims(string ns, string level)
    {
        var key = $"{ns}:{level}/anims.xml";
        if (!_loader.Vfs.TryGet(key, out var entry))
            return null;

        try { return _parser.ParseAnims(entry); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Render] Failed to parse {key}: {ex.Message}");
            return null;
        }
    }

    private ObjectsFile? TryParseObjects(string ns, string level)
    {
        var key = $"{ns}:{level}/objects.xml";
        if (!_loader.Vfs.TryGet(key, out var entry))
            return null;

        try { return ObjectsFileParser.Parse(entry); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Render] Failed to parse {key}: {ex.Message}");
            return null;
        }
    }

    private GfxDataFile? TryParseGfxData(string ns, string level)
    {
        var key = $"{ns}:{level}/gfxdata.xml";
        if (!_loader.Vfs.TryGet(key, out var entry))
            return null;

        try { return _parser.ParseGfxData(entry); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Render] Failed to parse {key}: {ex.Message}");
            return null;
        }
    }

    private void BuildObjectRoomMap(LevelLayout layout)
    {
        _objectRooms.Clear();

        foreach (var obj in layout.Objects)
            _objectRooms[obj.Name] = string.Empty;

        foreach (var room in layout.Rooms)
        {
            foreach (var obj in room.Objects)
                _objectRooms[obj.Name] = room.Name;
        }
    }

    private void OnActorSpriteChanged(string actorName, string spriteName) =>
        _actorSprites[actorName] = spriteName;

    private void OnObjectSpriteChanged(string objectName, string spriteName) =>
        _objectSprites[objectName] = spriteName;

    private void OnSoundTriggered(string sfxPath)
    {
        var soundName = Path.GetFileName(sfxPath);
        if (!string.IsNullOrEmpty(soundName))
            _sfx.Player?.PlaySound(soundName);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = (int)_clock.ElapsedMilliseconds;
        _clock.Restart();

        if (elapsed < 1) elapsed = 1;
        if (elapsed > 100) elapsed = 100;

        _logic.Update(elapsed);

        if (_statusUntilMs > 0)
            _statusUntilMs -= elapsed;

        // Initialise camera to Woody's spawn position on first tick
        if (!_camInitialized)
        {
            var wo = _logic.World?.GetActor("woody", includeOlga: true);
            if (wo != null)
            {
                _camX = wo.Position.X;
                _camY = wo.Position.Y;
                _camInitialized = true;
            }
        }

        // Free camera scroll: arrow keys
        float camDelta = CamScrollSpeed * elapsed / 1000f;
        if (IsKeyHeld(Keys.Left)  || IsKeyHeld(Keys.A)) _camX -= camDelta;
        if (IsKeyHeld(Keys.Right) || IsKeyHeld(Keys.D)) _camX += camDelta;
        if (IsKeyHeld(Keys.Up)    || IsKeyHeld(Keys.W)) _camY -= camDelta;
        if (IsKeyHeld(Keys.Down)  || IsKeyHeld(Keys.S)) _camY += camDelta;

        // Free camera scroll: mouse at screen edges
        var mouse = PointToClient(System.Windows.Forms.Cursor.Position);
        if (mouse.X < EdgeScrollZone)              _camX -= camDelta;
        if (mouse.X > ClientSize.Width - EdgeScrollZone)  _camX += camDelta;
        if (mouse.Y < EdgeScrollZone)              _camY -= camDelta;
        if (mouse.Y > ClientSize.Height - EdgeScrollZone) _camY += camDelta;

        Invalidate();
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        var world = _logic.World;
        if (world == null)
            return;

        var woody = world.GetActor("woody", includeOlga: true);
        if (woody == null)
            return;

        var camera = new NfhPoint((int)_camX, (int)_camY);
        var clickWorld = ScreenToWorld(e.Location, camera);

        if (e.Button == MouseButtons.Right)
        {
            _logic.ReportWoodySpotted();
            SetStatus("Debug: WoodySpotted forced");
            return;
        }

        if (e.Button != MouseButtons.Left)
            return;

        var clickedObject = _objectHitAreas
            .FirstOrDefault(p => p.Value.Contains(e.Location));

        if (!string.IsNullOrEmpty(clickedObject.Key))
        {
            _logic.ReportNoise(1);
            SetStatus($"Interacted with {clickedObject.Key}");
            return;
        }

        var targetRoom = FindNearestRoom(clickWorld);
        if (targetRoom == null)
            return;

        _logic.Actors?.NavigateTo("woody", targetRoom.Name);
        SetStatus($"Move Woody to {targetRoom.Name}");
    }

    private Room? FindNearestRoom(NfhPoint worldPoint)
    {
        var rooms = _logic.World?.Layout.Rooms;
        if (rooms == null || rooms.Count == 0)
            return null;

        Room? best = null;
        long bestDist = long.MaxValue;

        foreach (var room in rooms)
        {
            // Path1/Path2 are LOCAL room coordinates -- add room.Offset to get world coords
            var worldMidX = room.Offset.X + (room.Path1.X + room.Path2.X) / 2;
            var worldMidY = room.Offset.Y + (room.Path1.Y + room.Path2.Y) / 2;
            var dx = (long)worldPoint.X - worldMidX;
            var dy = (long)worldPoint.Y - worldMidY;
            var dist = dx * dx + dy * dy;

            if (dist < bestDist)
            {
                bestDist = dist;
                best = room;
            }
        }

        return best;
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var world = _logic.World;
        if (world == null)
        {
            e.Graphics.Clear(Color.Black);
            return;
        }

        var woody = world.GetActor("woody", includeOlga: true);
        if (woody == null)
        {
            e.Graphics.Clear(Color.Black);
            using var brush = new SolidBrush(Color.WhiteSmoke);
            e.Graphics.DrawString("Woody actor is missing in level layout.", Font, brush, 16, 16);
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.Clear(Color.FromArgb(30, 35, 42));

        var room = world.Layout.Rooms
            .FirstOrDefault(r => r.Name.Equals(woody.CurrentRoom, StringComparison.OrdinalIgnoreCase));

        var camera = new NfhPoint((int)_camX, (int)_camY);

        DrawRoomGuides(e.Graphics, room, camera);
        DrawSceneSprites(e.Graphics, room, camera);
        DrawHud(e.Graphics, world, room, woody);
    }

    private void DrawRoomGuides(Graphics g, Room? currentRoom, NfhPoint camera)
    {
        var world = _logic.World;
        if (world == null) return;

        using var floorBrush = new SolidBrush(Color.FromArgb(48, 74, 98));
        using var floorPen   = new Pen(Color.FromArgb(90, 128, 160));
        using var activePen  = new Pen(Color.FromArgb(110, 160, 200));

        // Draw ALL rooms' floors (world is bigger than the viewport)
        foreach (var room in world.Layout.Rooms)
        {
            bool isActive = room == currentRoom;
            foreach (var floor in room.Floors)
            {
                var worldRect = new Rectangle(
                    room.Offset.X + floor.Offset.X,
                    room.Offset.Y + floor.Offset.Y,
                    Math.Max(1, floor.Size.X),
                    Math.Max(1, floor.Size.Y));

                var screenRect = WorldToScreen(worldRect, camera);
                // Only draw if on screen (rough cull)
                if (screenRect.Right < 0 || screenRect.Left > ClientSize.Width) continue;
                if (screenRect.Bottom < 0 || screenRect.Top  > ClientSize.Height) continue;

                g.FillRectangle(floorBrush, screenRect);
                g.DrawRectangle(isActive ? activePen : floorPen, screenRect);
            }
        }
    }

    private void DrawHouseBackground(Graphics g, WorldState world, NfhPoint camera)
    {
        // house is a world object at position (0,0).
        // Draw it camera-relative like all other world objects so the background
        // stays fixed in world space when the camera scrolls.
        if (!world.Objects.TryGetValue("house", out var houseObj) || !houseObj.Visible)
            return;

        var sprite = ResolveObjectSprite("house");
        if (string.IsNullOrEmpty(sprite)) return;

        var bmp = _gfx.GetBitmap("house", sprite);
        if (bmp == null) return;

        // house world position is (0,0); anchor is top-left corner of the sprite
        var drawW = (int)(bmp.Width  * Scale);
        var drawH = (int)(bmp.Height * Scale);
        var x = WorldToScreenX(0, camera);
        var y = WorldToScreenY(0, camera);
        if (Scale == 1.0f)
            g.DrawImageUnscaled(bmp, x, y);
        else
            g.DrawImage(bmp, new Rectangle(x, y, drawW, drawH));
    }

    private void DrawSceneSprites(Graphics g, Room? room, NfhPoint camera)
    {
        _objectHitAreas.Clear();

        var world = _logic.World;
        if (world == null)
            return;

        // Draw background (house) first at world position (0,0) with camera offset.
        // All other objects are drawn on top in world space too.
        DrawHouseBackground(g, world, camera);

        var items = new List<RenderItem>();

        foreach (var obj in world.Objects.Values)
        {
            // Skip house -- already drawn as background
            if (obj.Name.Equals("house", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!obj.Visible)
                continue;

            // Show all objects in all rooms (world scrolls, all rooms coexist)
            var sprite = ResolveObjectSprite(obj.Name);
            items.Add(new RenderItem(obj.Name, obj.Layer, obj.Position, sprite, IsActor: false));
        }

        // Also add doors
        foreach (var door in world.Doors.Values)
        {
            if (!door.Visible) continue;
            var sprite = ResolveObjectSprite(door.Name);
            items.Add(new RenderItem(door.Name, door.Layer, door.Position, sprite, IsActor: false));
        }

        foreach (var actor in EnumerateActors(world))
        {
            if (actor == null) continue;
            var sprite = ResolveActorSprite(actor.Name);
            items.Add(new RenderItem(actor.Name, actor.Layer, actor.Position, sprite, IsActor: true));
        }

        foreach (var item in items.OrderBy(i => i.Layer).ThenBy(i => i.Position.Y))
            DrawRenderItem(g, item, camera);
    }

    private void DrawRenderItem(Graphics g, RenderItem item, NfhPoint camera)
    {
        Rectangle bounds;

        if (!string.IsNullOrEmpty(item.SpriteName))
        {
            // Use owner-qualified lookup so "topleft/buffet/ms_0000.tga" is found
            // instead of the wrong "ms_0000.tga" from another object's folder.
            var bmp = _gfx.GetBitmap(item.OwnerName, item.SpriteName);
            if (bmp != null)
            {
                var offset = GetSpriteOffset(item.OwnerName, item.SpriteName);
                var worldX = item.Position.X + offset.X;
                var worldY = item.Position.Y + offset.Y;

                var x = WorldToScreenX(worldX - bmp.Width / 2, camera);
                var y = WorldToScreenY(worldY - bmp.Height, camera);

                var drawW = (int)(bmp.Width  * Scale);
                var drawH = (int)(bmp.Height * Scale);
                bounds = new Rectangle(x, y, drawW, drawH);
                if (Scale == 1.0f)
                    g.DrawImageUnscaled(bmp, bounds.Location);
                else
                    g.DrawImage(bmp, bounds);

                if (!item.IsActor)
                    _objectHitAreas[item.OwnerName] = bounds;

                return;
            }

            if (_missingSpriteWarnings.Add(item.OwnerName + ":" + item.SpriteName))
                Console.Error.WriteLine($"[Render] Missing bitmap for '{item.OwnerName}' sprite '{item.SpriteName}'");
        }

        var p = ScreenPoint(item.Position, camera);
        bounds = new Rectangle(p.X - 10, p.Y - 22, 20, 22);

        using var brush = new SolidBrush(item.IsActor ? Color.Orange : Color.CadetBlue);
        using var pen = new Pen(Color.Black);
        g.FillRectangle(brush, bounds);
        g.DrawRectangle(pen, bounds);

        using var textBrush = new SolidBrush(Color.WhiteSmoke);
        g.DrawString(item.OwnerName, Font, textBrush, p.X + 12, p.Y - 20);

        if (!item.IsActor)
            _objectHitAreas[item.OwnerName] = bounds;
    }

    private void DrawHud(Graphics g, WorldState world, Room? room, ActorInstance woody)
    {
        using var bgBrush = new SolidBrush(Color.FromArgb(170, 16, 20, 26));
        using var fgBrush = new SolidBrush(Color.WhiteSmoke);
        using var accent = new SolidBrush(Color.FromArgb(220, 140, 210, 255));

        var hudRect = new Rectangle(8, 8, 520, 78);
        g.FillRectangle(bgBrush, hudRect);

        g.DrawString($"Room: {woody.CurrentRoom}  Woody: {woody.Position.X}/{woody.Position.Y}", Font, fgBrush, 16, 16);
        g.DrawString($"Camera: {(int)_camX}/{(int)_camY}  Objects: {_objectHitAreas.Count}", Font, fgBrush, 16, 36);

        if (_statusUntilMs > 0)
            g.DrawString(_statusText, Font, accent, 16, 56);

        if (room != null)
            g.DrawString($"[{room.Name}] path {room.Path1.X}/{room.Path1.Y}->{room.Path2.X}/{room.Path2.Y}", Font, fgBrush, 16, 56);
    }

    private bool IsObjectInRoom(string objectName, string? roomName)
    {
        if (!_objectRooms.TryGetValue(objectName, out var mappedRoom))
            return true;

        if (string.IsNullOrEmpty(mappedRoom))
            return true;

        if (string.IsNullOrEmpty(roomName))
            return false;

        return mappedRoom.Equals(roomName, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<ActorInstance> EnumerateActors(WorldState world)
    {
        // Woody always first (player character)
        var woody = world.GetActor("woody", includeOlga: true);
        if (woody != null) yield return woody;

        // Neighbor only if present on this level (null on tutorials)
        if (world.Neighbor != null) yield return world.Neighbor;

        if (world.Mother != null) yield return world.Mother;
        if (world.Olga   != null) yield return world.Olga;

        foreach (var actor in world.Actors.Values)
            if (actor != null) yield return actor;
    }

    private NfhPoint GetSpriteOffset(string ownerName, string spriteName)
    {
        if (_ownerSpriteOffsets.TryGetValue(ownerName, out var map) &&
            map.TryGetValue(spriteName, out var offset))
            return offset;

        return NfhPoint.Zero;
    }

    private Rectangle WorldToScreen(Rectangle worldRect, NfhPoint camera) => new(
        WorldToScreenX(worldRect.X, camera),
        WorldToScreenY(worldRect.Y, camera),
        (int)(worldRect.Width  * Scale),
        (int)(worldRect.Height * Scale));

    private Point ScreenPoint(NfhPoint worldPoint, NfhPoint camera) => new(
        WorldToScreenX(worldPoint.X, camera),
        WorldToScreenY(worldPoint.Y, camera));

    private int WorldToScreenX(int worldX, NfhPoint camera) =>
        (ClientSize.Width / 2) + (int)((worldX - camera.X) * Scale);

    private int WorldToScreenY(int worldY, NfhPoint camera) =>
        (ClientSize.Height / 2) + (int)((worldY - camera.Y) * Scale);

    /// <summary>
    /// Converts a screen point back to world coordinates.
    /// Inverse of WorldToScreen: accounts for scale and camera offset.
    /// </summary>
    private NfhPoint ScreenToWorld(Point screenPoint, NfhPoint camera) => new(
        (int)((screenPoint.X - ClientSize.Width  / 2) / Scale) + camera.X,
        (int)((screenPoint.Y - ClientSize.Height / 2) / Scale) + camera.Y);

    private bool IsKeyHeld(Keys key) => _heldKeys.Contains(key);

    private void SetStatus(string text)
    {
        _statusText = text;
        _statusUntilMs = 2500;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();

            var anim = _logic.Animations;
            if (anim != null)
            {
                anim.ActorSpriteChanged -= OnActorSpriteChanged;
                anim.ObjectSpriteChanged -= OnObjectSpriteChanged;
                anim.SoundTriggered -= OnSoundTriggered;
            }
        }

        base.Dispose(disposing);
    }

    private sealed record RenderItem(
        string OwnerName,
        int Layer,
        NfhPoint Position,
        string? SpriteName,
        bool IsActor);
}
