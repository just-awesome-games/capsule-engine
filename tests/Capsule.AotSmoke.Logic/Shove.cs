namespace Capsule.AotSmoke.Logic;

// A platform sweeping back and forth across a body that walks into it: the shove path, which sorts its
// candidates on every move of the platform. The shell steps the pair and counts what the steps allocate.
public sealed class ShoveLift : Entity
{
    public const string Layer = "platform";

    private const float Speed = 2f;
    private const float Reach = 64f;

    private readonly float _start;
    private float _direction = 1f;

    public ShoveLift(Vector2 position)
        : base(position)
    {
        _start = position.X;
        Add(new BoxCollider2D(new Vector2(32f, 8f)) { Layer = Layer });
    }

    protected override void OnStep(in StepContext context)
    {
        float x = Position.X + (_direction * Speed);
        if (x <= _start || x >= _start + Reach)
        {
            _direction = -_direction;
        }

        Position = new Vector2(x, Position.Y);
    }
}

public sealed class ShovedBody : Entity
{
    private readonly KinematicBody2D _mover;
    private float _last;

    // The steps the body ended to the right of where it ended the one before. It only walks left, and
    // each of them was a shove.
    public int Shoves { get; private set; }

    public ShovedBody(Vector2 position)
        : base(position)
    {
        _last = position.X;
        BoxCollider2D collider = new(new Vector2(12f, 12f));
        Add(collider);
        _mover = new KinematicBody2D(collider) { Mode = BodyMode.Floating };
        _mover.BlockedBy = new(ShoveLift.Layer);
        _mover.MovedBy = new(ShoveLift.Layer);
        Add(_mover);
    }

    protected override void OnStep(in StepContext context)
    {
        if (Position.X > _last)
        {
            Shoves++;
        }

        _last = Position.X;
        _mover.Move(new Vector2(-1f, 0f));
    }
}
