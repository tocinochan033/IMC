using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace IMC_.Services;

public sealed class SupabaseSessionPersistence(ProtectedSessionStorage storage)
    : IGotrueSessionPersistence<Session>
{
    private const string StorageKey = "supabase.auth.session";
    private Session? currentSession;

    public void SaveSession(Session session)
    {
        currentSession = session;
    }

    public void DestroySession()
    {
        currentSession = null;
    }

    public Session? LoadSession() => currentSession;

    public async Task SaveSessionAsync(Session session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        currentSession = session;
        await storage.SetAsync(StorageKey, session);
    }

    public async Task DestroySessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        currentSession = null;
        await storage.DeleteAsync(StorageKey);
    }

    public async Task<Session?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await storage.GetAsync<Session>(StorageKey);
        currentSession = result.Success ? result.Value : null;
        return currentSession;
    }
}
