using System.Globalization;
using System.Text.Json;
using Capsule;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Verify;
using PackageConsumer.Game;

namespace PackageConsumer.Verify;

/// <summary>
/// The artifact half of the seam: a state dump a build can diff and a frame dump standing in for
/// a screenshot. A game rasterises the render intent here; the consumer only has to prove the
/// sink is called with something it can write.
/// </summary>
internal sealed class VerifyArtifactSink(string outputDirectory) : IVerifyArtifactSink
{
    public void WriteStateDump(ISimulation simulation, in VerifyRunResult result)
    {
        SceneSimulation scene = (SceneSimulation)simulation;

        Directory.CreateDirectory(outputDirectory);
        using FileStream stream = File.Create(Path.Combine(outputDirectory, "state.json"));
        using Utf8JsonWriter json = new(stream, new JsonWriterOptions { Indented = true });

        json.WriteStartObject();
        json.WriteNumber("completedSteps", result.CompletedSteps);
        json.WriteNumber("measuredSteps", result.MeasuredSteps);
        json.WriteBoolean("exitRequested", result.ExitRequested);
        json.WriteNumber("allocatedBytes", result.AllocatedBytes);
        json.WriteNumber("peakFrameAllocatedBytes", result.PeakFrameAllocatedBytes);
        json.WriteNumber("markerX", scene.Scene.FindSingle<Marker>().Position.X);
        json.WriteEndObject();
    }

    public void CaptureScreenshot(FrameView view, in VerifyRunResult result)
    {
        ArgumentNullException.ThrowIfNull(view);
        _ = result;

        Directory.CreateDirectory(outputDirectory);
        using StreamWriter frame = new(Path.Combine(outputDirectory, "frame.txt"));

        CameraView camera = view.Camera;
        frame.WriteLine(FormattableString.Invariant(
            $"camera {camera.PreviousCenter.X},{camera.PreviousCenter.Y} -> {camera.Center.X},{camera.Center.Y} span {camera.Size.X}x{camera.Size.Y}"));

        foreach (QuadIntent quad in view.Quads)
        {
            frame.WriteLine(FormattableString.Invariant(
                $"quad {quad.Position.X},{quad.Position.Y} {quad.Size.X}x{quad.Size.Y} #{quad.Color.R:x2}{quad.Color.G:x2}{quad.Color.B:x2}{quad.Color.A:x2}"));
        }

        frame.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"quads {view.Metrics.VisibleQuads}/{view.Metrics.TotalQuads}"));
    }
}
