namespace Capsule.Persistence;

/// <summary>
/// When a document was first and last persisted: the host's wall clock at the flush, with its local
/// offset. Host state, never simulation input.
/// </summary>
/// <param name="CreatedAt">The first flush that persisted the document, carried forward by every later one.</param>
/// <param name="UpdatedAt">The most recent flush that persisted it.</param>
public readonly record struct SaveMetadata(DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
