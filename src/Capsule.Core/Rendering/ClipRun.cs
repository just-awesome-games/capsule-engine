namespace Capsule.Rendering;

// The canvas-pixel rect one clip level shows at the two ends of a step. The renderer interpolates between
// them as it does a sprite's position.
internal readonly record struct ClipRegion(Rect Previous, Rect Current);

// One run of the screen layer's sprites and lines drawn under one clip, from FirstSprite and FirstLine up
// to the next run's or the end of each list. The clip is the intersection of Levels regions, one per nested
// ClipRect, held from FirstLevel in the layer's level list. Zero levels is no clip. Sprites and lines before
// the first run draw with none. A run carries where it starts in both lists, since lines draw after every
// sprite. The levels stay apart until the renderer interpolates them, as intersecting first over-clips a
// moving inner level.
internal readonly record struct ClipRun(int FirstSprite, int FirstLine, int FirstLevel, int Levels);
