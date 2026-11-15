namespace Capsule.Bench.Logic.Entities;

/// <summary>A drifting sensor that counts the actors it touches and turns back at the edges of its room, as a moving field or a swarm member would.</summary>
/// <remarks>Given a period, it leaves the collision world and joins it again once a period at its phase, as a pooled sensor despawned and spawned again would.</remarks>
public sealed class Seeker : Entity
{
    private readonly BoxCollider2D _area;
    private readonly Vector2 _room;
    private readonly int _period;
    private readonly int _phase;
    private Vector2 _velocity;

    public int Occupants { get; private set; }

    public Seeker(Vector2 position, Vector2 velocity, Vector2 size, Vector2 room, int period = 0, int phase = 0)
        : base(position)
    {
        _velocity = velocity;
        _room = room;
        _period = period;
        _phase = phase;

        _area = new(size) { Layer = CollisionLayers.Sensor, ReportsContacts = true };
        _area.Detects = new(CollisionLayers.Actor);
        _area.ContactEntered += _ => Occupants++;
        _area.ContactExited += _ => Occupants--;
        Add(_area);
    }

    protected override void OnStep(in StepContext context)
    {
        if (_period > 0 && context.Tick % _period == _phase)
        {
            _area.Enabled = false;
            _area.Enabled = true;
        }

        Vector2 next = Position + _velocity;
        if (next.X < 0f || next.X > _room.X)
        {
            _velocity.X = -_velocity.X;
        }

        if (next.Y < 0f || next.Y > _room.Y)
        {
            _velocity.Y = -_velocity.Y;
        }

        Position = Vector2.Clamp(next, Vector2.Zero, _room);
    }
}
