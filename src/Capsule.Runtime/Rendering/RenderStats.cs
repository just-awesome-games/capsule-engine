namespace Capsule.Runtime.Rendering;

// What one game frame cost the renderer: wall-clock milliseconds across its submission. Submission
// only, excluding the present and its vsync wait.
internal readonly record struct RenderStats(double Milliseconds);
