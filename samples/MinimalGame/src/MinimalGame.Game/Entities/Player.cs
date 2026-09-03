using System.Numerics;
using Capsule;
using Capsule.Assets.Generated;
using Capsule.Diagnostics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Physics;
using Capsule.Scenes.Spawning;

namespace MinimalGame.Game.Entities;

/// <summary>
/// The walking, falling, jumping body spawned by the <c>player</c> entries of
/// <c>scenes/room.tmj</c> and <c>scenes/hall.scene.json</c>.
/// <para>
/// A concrete entity with one public constructor taking an <see cref="EntitySpawn"/> claims its
/// kebab-cased class name as the document entry type it spawns from, so <c>Player</c> answers to
/// <c>"player"</c> with nothing registered by hand; <c>[SpawnType("...")]</c> renames the claim
/// without renaming the class.
/// </para>
/// <para>
/// <see cref="Entity.Position"/> is the top-left corner of the 8x8 body: the box collider is
/// corner-anchored, and the sprite anchors its frame's top-centre at that same pivot offset from
/// the corner, so the frame covers the body facing either way and a Tiled point object's
/// coordinate is taken as that corner here. Anchoring an authored coordinate is each entity's own
/// convention.
/// </para>
/// <para>
/// The frame is drawn by a <see cref="SpriteRenderer"/> whose <see cref="SpriteRenderer.FlipX"/>
/// the walk direction sets, so one texture faces both ways: a flip mirrors the frame about its
/// pivot, and the pivot is the point <see cref="SpriteRenderer.Offset"/> puts on the body.
/// </para>
/// <para>
/// The two collision filters are independent. <see cref="KinematicBody2D.BlocksOn"/> names what
/// stops the sweep — <c>solid</c> and <c>platform</c>, the layers the room's tiles are authored on
/// — while <see cref="Collider2D.Detects"/> names what the collider reports, which is <c>sensor</c>
/// alone: the player walks through a <see cref="Sensor"/> and says so. Contacts, jumps and landings
/// are logged through <see cref="Log"/>, which the shell drains to the console at boot, each line
/// prefixed with the tick it happened on.
/// </para>
/// </summary>
public sealed class Player : Entity
{
    /// <summary>World units per second.</summary>
    private const float WalkSpeed = 80f;

    /// <summary>World units per second squared, downwards in a Y-down world.</summary>
    private const float Gravity = 600f;

    /// <summary>World units per second at take-off; an apex of about 40px, clearing a two-tile ledge.</summary>
    private const float JumpSpeed = 220f;

    /// <summary>The body's edge in world units, and the frame's in texels: one texel per unit.</summary>
    private const int BodyPixels = 8;

    private static readonly Vector2 Body = new(BodyPixels, BodyPixels);

    /// <summary>
    /// The frame's pivot, in texels from its top-left corner, and the same vector from the body's
    /// corner to the point it anchors. Halfway across the region on purpose: a flip mirrors the
    /// frame about its pivot, so anchoring the horizontal centre keeps the drawn frame over the
    /// corner-anchored collider in both facings. Both derive from the region's own width.
    /// </summary>
    private static readonly Vector2 Pivot = new(BodyPixels / 2f, 0f);

    /// <summary>The whole of <c>textures/player.png</c>.</summary>
    private static readonly Sprite Idle =
        new(GameAssets.Textures.Player, new TextureRegion(0, 0, BodyPixels, BodyPixels), Pivot);

    private readonly SpriteRenderer _sprite;
    private readonly KinematicBody2D _body;

    private Vector2 _velocity;

    public Player(EntitySpawn spawn)
        : base(spawn.Position)
    {
        _sprite = new SpriteRenderer(Idle) { Offset = Pivot };
        Add(_sprite);

        BoxCollider2D collider = new(Body);
        collider.Detects("sensor");
        collider.ReportsContacts = true;
        collider.ContactEntered += contact =>
            Log.Info(FormattableString.Invariant($"entered {contact.LayerName} at {contact.Point}"));
        collider.ContactExited += contact =>
            Log.Info(FormattableString.Invariant($"exited {contact.LayerName} at {contact.Point}"));
        Add(collider);

        _body = new KinematicBody2D(collider);
        _body.BlocksOn("solid", "platform");
        Add(_body);
    }

    /// <inheritdoc/>
    protected override void OnStep(in StepContext context)
    {
        float delta = context.DeltaSeconds;

        // The body applies no forces: velocity is the game's, every step.
        _velocity.X = context.Input.Axis(GameInput.Move) * WalkSpeed;
        _velocity.Y += Gravity * delta;

        // Facing is kept through a standstill, so the player stops looking where it walked.
        if (_velocity.X != 0f)
        {
            _sprite.FlipX = _velocity.X < 0f;
        }

        // IsOnFloor is state as of the last Move, so this reads the previous step's landing.
        bool wasOnFloor = _body.IsOnFloor;
        if (wasOnFloor && context.Input.WasPressed(GameInput.Jump))
        {
            _velocity.Y = -JumpSpeed;
            Log.Info("jumped");
        }

        _body.Move(_velocity * delta);

        if (_body.IsOnFloor)
        {
            _velocity.Y = 0f;

            if (!wasOnFloor)
            {
                // The room's ledges collide on their top face alone, so landing on one names
                // 'platform' here while a jump up through it names nothing at all.
                foreach (ColliderContact2D contact in _body.MoveContacts)
                {
                    if (contact.Normal.Y < 0f)
                    {
                        Log.Info("landed on " + contact.LayerName);
                        break;
                    }
                }
            }
        }

        if (_body.IsOnCeiling)
        {
            _velocity.Y = 0f;
        }
    }
}
