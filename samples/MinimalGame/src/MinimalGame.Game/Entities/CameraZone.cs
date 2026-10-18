namespace MinimalGame.Game.Entities;

/// <summary>
/// An invisible area that holds the camera inside it while the player stands in it, and hands back
/// the bounds held before the first zone when the player leaves every zone. The view slides in on a set
/// curve and eases back out on the camera's own chase.
/// </summary>
/// <remarks>The first slide-in takes control from the player until the view lands.</remarks>
public sealed class CameraZone : Entity
{
    private static readonly BoundsTransition SlideIn = BoundsTransition.Eased(0.5f, Ease.InOutSine);

    private bool _holdsPlayer;

    // Whether the player left this step, for the late step to hand the bounds on.
    private bool _left;

    private Rect? _outside;

    // Whether the view has framed this zone once, and the player held while it first does.
    private bool _framed;
    private Player? _held;

    /// <summary>The area's width and height in world units.</summary>
    [Authorable(Required = true)]
    public Vector2 Size { get; private set; }

    /// <param name="spawn">The area's top-left corner.</param>
    public CameraZone(EntitySpawn spawn)
        : base(spawn)
    {
        BoxCollider2D area = new(Size) { ReportsContacts = true, Detects = new(CollisionLayers.Player) };
        area.ContactEntered += OnPlayerEntered;
        area.ContactExited += OnPlayerExited;
        Add(area);
    }

    // Stepping in from a neighbouring zone carries over the bounds that zone found, even one the player left
    // earlier in this step's settle.
    private void OnPlayerEntered(ColliderContact2D contact)
    {
        _outside = Scene.FindFirst<CameraZone>(zone => zone._holdsPlayer || zone._left) is { } held ? held._outside : Scene.Camera.Bounds;
        _holdsPlayer = true;
        Scene.Camera.SetBounds(new Rect(Position, Size), SlideIn);

        if (!_framed)
        {
            _framed = true;
            _held = Scene.FindSingle<Player>();
            _held.HasControl = false;
        }
    }

    // A scene tearing down with the player inside ends the contact after this zone has left it. The
    // hand-back then waits for a late step that never comes.
    private void OnPlayerExited(ColliderContact2D contact)
    {
        _holdsPlayer = false;
        _left = true;
    }

    // Leaving into a zone the player still stands in hands the bounds to that zone, and leaving every zone
    // brings back the bounds held before the first. Contacts settle one collider at a time. The hand-back
    // waits until every zone's have, whichever order they registered in.
    protected override void OnLateStep(in StepContext context)
    {
        if (_held is { } player && !Scene.Camera.IsTransitioning)
        {
            player.HasControl = true;
            _held = null;
        }

        if (!_left)
        {
            return;
        }

        _left = false;
        if (Scene.FindFirst<CameraZone>(zone => zone._holdsPlayer) is { } held)
        {
            Scene.Camera.SetBounds(new Rect(held.Position, held.Size), SlideIn);
        }
        else
        {
            Scene.Camera.Bounds = _outside;
        }
    }
}
