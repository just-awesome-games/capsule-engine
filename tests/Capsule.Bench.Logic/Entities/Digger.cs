namespace Capsule.Bench.Logic.Entities;

/// <summary>
/// Digs out and fills back cells of a rule tile map every step, each paint repicking the rule tiles around it.
/// </summary>
/// <remarks>
/// It walks the whole map by a stride that visits every cell. Passes alternate between digging every cell out and
/// filling every cell back, emptying and refilling each block.
/// </remarks>
public sealed class Digger(TileMap map, string fill) : Entity(Vector2.Zero)
{
    private static readonly int PerStep = Workloads.Scaled(64);

    // An odd stride visits every cell of a map whose area is a power of two before repeating one.
    private const int Stride = 40_503;

    private int _painted;

    protected override void OnStep(in StepContext context)
    {
        int area = map.Width * map.Height;
        for (int index = 0; index < PerStep; index++, _painted++)
        {
            int cell = (int)((long)_painted * Stride % area);
            bool dug = _painted / area % 2 == 0;
            map.SetTile(cell % map.Width, cell / map.Width, dug ? TileGrid.EmptyTileName : fill);
        }
    }
}
