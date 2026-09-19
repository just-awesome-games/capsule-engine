namespace Capsule.Persistence;

/// <summary>
/// The medium a run's save documents are kept on. The host calls it on its own thread between steps,
/// restoring once at boot and persisting or removing after each step. The desktop medium is
/// <c>Capsule.Runtime.Persistence.DirectorySaveStorage</c>, and a platform that mounts a container
/// replaces it through <c>EngineBuilder.WithSaveStorage</c>.
/// </summary>
public interface ISaveStorage
{
    /// <summary>
    /// Hands every document held to <paramref name="restore"/> in any order, as a name, the JSON as
    /// persisted and its metadata. A document the medium cannot read in full is set aside and
    /// reported.
    /// </summary>
    void Restore(Action<string, string, SaveMetadata> restore);

    /// <summary>
    /// Writes the document under <paramref name="name"/>, replacing what was there. The host logs a
    /// throw once and drops the write until the name is written again.
    /// </summary>
    void Persist(string name, string document, SaveMetadata metadata);

    /// <summary>Removes the document and whatever the medium keeps beside it. Does nothing when absent.</summary>
    void Delete(string name);
}
