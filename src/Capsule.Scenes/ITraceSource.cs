namespace Capsule.Scenes;

/// <summary>
/// Something that contributes its own columns to a <see cref="StateTrace"/>. An
/// <see cref="Entity"/> writes under its own subject; a <see cref="Component"/> writes under the
/// subject of the entity holding it, after that entity's own columns.
/// </summary>
public interface ITraceSource
{
    /// <summary>
    /// Writes this source's columns for the step that just ran. Called once per traced step while
    /// the trace is on, and never at all while it is off, so it may cost whatever a diagnostic
    /// costs. Two sources on one subject writing the same column name produce two rows for it.
    /// </summary>
    /// <param name="writer">Where the columns go, already bound to the subject and the tick.</param>
    void WriteTrace(TraceWriter writer);
}
