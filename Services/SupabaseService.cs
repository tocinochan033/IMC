using Supabase;
using IMC_.Models;
using Operator = Supabase.Postgrest.Constants.Operator;

namespace IMC_.Services;

public enum ResultadoEnvioCorreo
{
    Enviado,
    DestinatarioNoValido
}

public class SupabaseService
{
    public Client Client { get; }
    private readonly Lazy<Task> initialization;
    public Task Initialization => initialization.Value;

    public SupabaseService(IConfiguration configuration, SupabaseSessionPersistence sessionPersistence)
    {
        var url = configuration["Supabase:Url"];
        var apiKey = configuration["Supabase:ApiKey"];

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("Supabase:Url must be configured.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Supabase:ApiKey must be configured.");
        }

        Client = new Client(url, apiKey, new SupabaseOptions
        {
            SessionHandler = sessionPersistence
        });
        initialization = new Lazy<Task>(Client.InitializeAsync);
    }

    public async Task SignInAsync(string email, string password)
    {
        await Initialization;
        await Client.Auth.SignIn(email, password);

        if (Client.Auth.CurrentSession?.User is null)
        {
            throw new InvalidOperationException("Supabase Auth no devolvió una sesión válida.");
        }
    }

    public async Task SignOutAsync()
    {
        await Initialization;
        await Client.Auth.SignOut();
    }

    public Task<List<(MailboxEntry Entry, Message Message)>> ObtenerBandejaEntradaAsync() =>
        ObtenerMensajesDeCarpetaAsync("inbox");

    public Task<List<(MailboxEntry Entry, Message Message)>> ObtenerEnviadosAsync() =>
        ObtenerMensajesDeCarpetaAsync("sent");

    public Task<List<(MailboxEntry Entry, Message Message)>> ObtenerPapeleraAsync() =>
        ObtenerMensajesDeCarpetaAsync("trash");

    public async Task<List<(MailboxEntry Entry, Message Message)>> ObtenerDestacados(Guid userId)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId) ||
            authenticatedUserId != userId)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar los destacados.");
        }

        var entriesResponse = await Client
            .From<MailboxEntry>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Filter("is_starred", Operator.Equals, "true")
            .Get();

        return await ObtenerMensajesDeEntradasAsync(entriesResponse.Models);
    }

    public async Task<List<(MailboxEntry Entry, Message Message)>> BuscarMensajes(
        Guid userId,
        string termino,
        string folder = "inbox")
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId) ||
            authenticatedUserId != userId)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para buscar mensajes.");
        }

        if (folder is not ("inbox" or "sent" or "starred"))
        {
            throw new ArgumentException("La carpeta de búsqueda no es válida.", nameof(folder));
        }

        var entriesQuery = Client
            .From<MailboxEntry>()
            .Filter("user_id", Operator.Equals, userId.ToString());

        if (folder == "starred")
        {
            entriesQuery = entriesQuery.Filter("is_starred", Operator.Equals, "true");
        }
        else
        {
            entriesQuery = entriesQuery.Filter("folder", Operator.Equals, folder);
        }

        var entriesResponse = await entriesQuery.Get();
        var entries = entriesResponse.Models;
        if (entries.Count == 0)
        {
            return [];
        }

        var messageIds = entries.Select(entry => entry.MessageId).Distinct().ToList();
        var pattern = $"%{EscaparPatronIlike(termino.Trim())}%";

        var subjectMatches = await Client
            .From<Message>()
            .Where(message => messageIds.Contains(message.Id))
            .Filter("subject", Operator.ILike, pattern)
            .Get();

        var senderProfiles = await Client
            .From<Profile>()
            .Filter("full_name", Operator.ILike, pattern)
            .Get();

        var senderIds = senderProfiles.Models
            .Select(profile => profile.Id)
            .Distinct()
            .ToList();

        var senderMatches = senderIds.Count == 0
            ? []
            : (await Client
                .From<Message>()
                .Where(message => messageIds.Contains(message.Id))
                .Where(message => senderIds.Contains(message.SenderId))
                .Get()).Models;

        var matchedMessages = subjectMatches.Models
            .Concat(senderMatches)
            .DistinctBy(message => message.Id)
            .ToList();

        return CombinarEntradasConMensajes(entries, matchedMessages);
    }

    private static string EscaparPatronIlike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private async Task<List<(MailboxEntry Entry, Message Message)>> ObtenerMensajesDeCarpetaAsync(string folder)
    {
        await Initialization;

        var userId = Client.Auth.CurrentSession?.User?.Id;
        if (!Guid.TryParse(userId, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar la bandeja.");
        }

        var entriesResponse = await Client
            .From<MailboxEntry>()
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Filter("folder", Operator.Equals, folder)
            .Get();

        return await ObtenerMensajesDeEntradasAsync(entriesResponse.Models);
    }

    private async Task<List<(MailboxEntry Entry, Message Message)>> ObtenerMensajesDeEntradasAsync(
        List<MailboxEntry> entries)
    {
        if (entries.Count == 0)
        {
            return [];
        }

        var messageIds = entries
            .Select(entry => entry.MessageId)
            .Distinct()
            .ToList();

        var messagesResponse = await Client
            .From<Message>()
            .Where(message => messageIds.Contains(message.Id))
            .Get();

        return CombinarEntradasConMensajes(entries, messagesResponse.Models);
    }

    private static List<(MailboxEntry Entry, Message Message)> CombinarEntradasConMensajes(
        List<MailboxEntry> entries,
        List<Message> messages)
    {
        var messagesById = messages.ToDictionary(message => message.Id);
        return entries
            .Where(entry => messagesById.ContainsKey(entry.MessageId))
            .Select(entry => (entry, messagesById[entry.MessageId]))
            .ToList();
    }

    public async Task ToggleDestacado(Guid mailboxEntryId, bool nuevoEstado)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para actualizar los destacados.");
        }

        await Client
            .From<MailboxEntry>()
            .Filter("id", Operator.Equals, mailboxEntryId.ToString())
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Set(entry => entry.IsStarred, nuevoEstado)
            .Update();
    }

    public async Task<Message?> ObtenerMensajePorIdAsync(string id)
    {
        await Initialization;

        if (!Guid.TryParse(id, out var messageId))
        {
            return null;
        }

        if (Client.Auth.CurrentSession?.User is null)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar el mensaje.");
        }

        var response = await Client
            .From<Message>()
            .Filter("id", Operator.Equals, messageId.ToString())
            .Get();

        return response.Models.FirstOrDefault();
    }

    public async Task<string?> ObtenerEmailPerfilAsync(Guid profileId)
    {
        await Initialization;

        if (Client.Auth.CurrentSession?.User is null)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar el perfil.");
        }

        var response = await Client
            .From<Profile>()
            .Filter("id", Operator.Equals, profileId.ToString())
            .Get();

        return response.Models.FirstOrDefault()?.Email;
    }

    public async Task<MailboxEntry?> ObtenerEntradaInboxPorMensajeAsync(string messageId)
    {
        await Initialization;

        if (!Guid.TryParse(messageId, out var parsedMessageId) ||
            !Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada y un ID de mensaje válido.");
        }

        var response = await Client
            .From<MailboxEntry>()
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Filter("message_id", Operator.Equals, parsedMessageId.ToString())
            .Filter("folder", Operator.Equals, "inbox")
            .Get();

        return response.Models.FirstOrDefault();
    }

    public async Task MarcarComoLeido(Guid mailboxEntryId)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para actualizar la bandeja.");
        }

        await Client
            .From<MailboxEntry>()
            .Filter("id", Operator.Equals, mailboxEntryId.ToString())
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Filter("folder", Operator.Equals, "inbox")
            .Set(entry => entry.IsRead, true)
            .Update();
    }

    public async Task MoverAPapelera(Guid mailboxEntryId)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para mover el mensaje.");
        }

        await Client
            .From<MailboxEntry>()
            .Filter("id", Operator.Equals, mailboxEntryId.ToString())
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Set(entry => entry.Folder, "trash")
            .Update();
    }

    public async Task<ResultadoEnvioCorreo> EnviarCorreo(
        Guid senderId,
        string destinatarioEmail,
        string subject,
        string body,
        Guid? parentMessageId = null)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId) ||
            authenticatedUserId != senderId)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada del remitente.");
        }

        var email = destinatarioEmail.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            return ResultadoEnvioCorreo.DestinatarioNoValido;
        }

        var profileResponse = await Client
            .From<Profile>()
            .Filter("email", Operator.Equals, email)
            .Get();

        var recipient = profileResponse.Models.FirstOrDefault();
        if (recipient is null)
        {
            return ResultadoEnvioCorreo.DestinatarioNoValido;
        }

        await Client.Rpc("send_email", new
        {
            p_sender_id = senderId,
            p_recipient_id = recipient.Id,
            p_subject = subject,
            p_body = body,
            p_parent_message_id = parentMessageId
        });

        return ResultadoEnvioCorreo.Enviado;
    }
}
