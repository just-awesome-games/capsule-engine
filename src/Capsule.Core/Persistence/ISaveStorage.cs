namespace Capsule.Persistence;

/// <summary>
/// The medium a run's save documents are kept on; the host calls it on its own thread between
/// steps, restoring once at boot and persisting or removing after each step. The desktop medium is
/// <c>Capsule.Runtime.Persistence.DirectorySaveStorage</c>; a platform that mounts a container
/// replaces it through <c>EngineBuilder.WithSaveStorage</c>.
/// </summary>
public interface ISaveStorage
{
    /// <summary>
    /// Hands every document held — name, JSON exactly as persisted, metadata — to <paramref name="restore"/>
    /// in any order; one the medium cannot read whole is set aside and reported, never handed over.
    /// </summary>
    void Restore(Action<string, string, SaveMetadata> restore);

    /// <summary>
    /// Writes the whole document under <paramref name="name"/>, replacing what was there. A throw is
    /// logged once by the host and the write dropped until the name is written again.
    /// </summary>
    void Persist(string name, string document, SaveMetadata metadata);

    /// <summary>Removes the document and whatever the medium keeps beside it; nothing when absent. A throw is logged once by the host.</summary>
    void Delete(string name);
}
