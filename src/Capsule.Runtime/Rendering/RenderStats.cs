namespace Capsule.Runtime.Rendering;

// What one game frame cost the renderer: wall-clock milliseconds from the start of its submission
// to the end. Submission only, never the present or its vsync wait.
internal readonly record struct RenderStats(double Milliseconds);
