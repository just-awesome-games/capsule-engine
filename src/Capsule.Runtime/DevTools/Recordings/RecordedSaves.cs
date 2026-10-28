using Capsule.Persistence;

namespace Capsule.Runtime.DevTools.Recordings;

// The run's medium with every document it restores copied into restored, for the recording's boot state.
internal sealed class TeeSaveStorage(ISaveStorage inner, List<RecordedSave> restored) : ISaveStorage
{
    public void Restore(Action<string, string, SaveMetadata> restore) =>
        inner.Restore((name, document, metadata) =>
        {
            restored.Add(new RecordedSave(name, document, metadata));
            restore(name, document, metadata);
        });

    public void Persist(string name, string document, SaveMetadata metadata) => inner.Persist(name, document, metadata);

    public void Delete(string name) => inner.Delete(name);
}

// A replay's medium: the recording's restored documents, with nothing persisted anywhere.
internal sealed class RecordedSaveStorage(IReadOnlyList<RecordedSave> saves) : ISaveStorage
{
    public void Restore(Action<string, string, SaveMetadata> restore)
    {
        foreach (RecordedSave save in saves)
        {
            restore(save.Name, save.Document, save.Metadata);
        }
    }

    public void Persist(string name, string document, SaveMetadata metadata)
    {
    }

    public void Delete(string name)
    {
    }
}
