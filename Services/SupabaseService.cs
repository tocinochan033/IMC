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
    public event Action? EtiquetasActualizadas;

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

    public Task<List<(MailboxEntry Entry, Message Message)>> ObtenerBorradoresAsync() =>
        ObtenerMensajesDeCarpetaAsync("drafts");

    public async Task<(MailboxEntry Entry, Message Message)?> ObtenerBorradorAsync(Guid messageId)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var userId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar el borrador.");
        }

        var entryResponse = await Client
            .From<MailboxEntry>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Filter("message_id", Operator.Equals, messageId.ToString())
            .Filter("folder", Operator.Equals, "drafts")
            .Get();

        var entry = entryResponse.Models.FirstOrDefault();
        if (entry is null)
        {
            return null;
        }

        var messageResponse = await Client
            .From<Message>()
            .Filter("id", Operator.Equals, messageId.ToString())
            .Get();

        var message = messageResponse.Models.FirstOrDefault();
        return message is null ? null : (entry, message);
    }

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

    public async Task<List<string>> ObtenerEtiquetas(Guid userId)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId) ||
            authenticatedUserId != userId)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar las etiquetas.");
        }

        var response = await Client
            .From<UserLabel>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Get();

        return response.Models
            .Select(entry => entry.Label)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task CrearEtiqueta(string etiqueta)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var userId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para crear etiquetas.");
        }

        var etiquetaNormalizada = etiqueta.Trim();
        if (string.IsNullOrWhiteSpace(etiquetaNormalizada) || etiquetaNormalizada.Length > 80)
        {
            throw new ArgumentException("La etiqueta debe contener entre 1 y 80 caracteres.", nameof(etiqueta));
        }

        await Client
            .From<UserLabel>()
            .Upsert(new UserLabel
            {
                UserId = userId,
                Label = etiquetaNormalizada
            }, new Supabase.Postgrest.QueryOptions
            {
                OnConflict = "user_id,label",
                DuplicateResolution = Supabase.Postgrest.QueryOptions.DuplicateResolutionType.IgnoreDuplicates
            });

        EtiquetasActualizadas?.Invoke();
    }

    public async Task<List<(MailboxEntry Entry, Message Message)>> ObtenerMensajesConEtiquetaAsync(
        Guid userId,
        string etiqueta)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId) ||
            authenticatedUserId != userId)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar las etiquetas.");
        }

        var entriesResponse = await Client
            .From<MailboxEntry>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Filter("folder", Operator.Equals, "inbox")
            .Filter("label", Operator.Equals, etiqueta)
            .Get();

        return await ObtenerMensajesDeEntradasAsync(entriesResponse.Models);
    }

    public async Task<List<(MailboxEntry Entry, Message Message)>> BuscarMensajes(
        Guid userId,
        string termino,
        string folder = "inbox",
        string? etiqueta = null)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId) ||
            authenticatedUserId != userId)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para buscar mensajes.");
        }

        if (folder is not ("inbox" or "sent" or "trash" or "drafts" or "starred"))
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
            if (folder == "inbox" && !string.IsNullOrWhiteSpace(etiqueta))
            {
                entriesQuery = entriesQuery.Filter("label", Operator.Equals, etiqueta);
            }
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

    public async Task AsignarEtiqueta(Guid mailboxEntryId, string etiqueta)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para asignar etiquetas.");
        }

        var etiquetaNormalizada = etiqueta.Trim();
        await Client
            .From<MailboxEntry>()
            .Filter("id", Operator.Equals, mailboxEntryId.ToString())
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Set(entry => entry.Label!, string.IsNullOrWhiteSpace(etiquetaNormalizada) ? null! : etiquetaNormalizada)
            .Update();

        EtiquetasActualizadas?.Invoke();
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

    public async Task<Profile?> ObtenerPerfilPorIdAsync(Guid profileId)
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

        return response.Models.FirstOrDefault();
    }

    public async Task<Dictionary<Guid, string>> ObtenerNombresRemitentesAsync(IEnumerable<Guid> senderIds)
    {
        await Initialization;

        if (Client.Auth.CurrentSession?.User is null)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para consultar los remitentes.");
        }

        var ids = senderIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var response = await Client
            .From<Profile>()
            .Where(profile => ids.Contains(profile.Id))
            .Get();

        return response.Models.ToDictionary(
            profile => profile.Id,
            profile => string.IsNullOrWhiteSpace(profile.FullName) ? profile.Email : profile.FullName);
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

    public async Task<bool> EliminarPermanentementeDePapelera(Guid mailboxEntryId)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para eliminar el mensaje.");
        }

        var response = await Client
            .From<MailboxEntry>()
            .Filter("id", Operator.Equals, mailboxEntryId.ToString())
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Filter("folder", Operator.Equals, "trash")
            .Delete();

        return response.Models.Count > 0;
    }

    public async Task<int> VaciarPapelera()
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId))
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada para vaciar la papelera.");
        }

        var response = await Client
            .From<MailboxEntry>()
            .Filter("user_id", Operator.Equals, authenticatedUserId.ToString())
            .Filter("folder", Operator.Equals, "trash")
            .Delete();

        return response.Models.Count;
    }

    public async Task<ResultadoEnvioCorreo> EnviarCorreo(
        Guid senderId,
        string destinatarioEmail,
        string subject,
        string body,
        Guid? parentMessageId = null,
        Guid? draftMessageId = null)
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
            p_parent_message_id = parentMessageId,
            p_draft_message_id = draftMessageId
        });

        return ResultadoEnvioCorreo.Enviado;
    }

    public async Task<Guid> GuardarBorrador(
        Guid senderId,
        string destinatarioEmail,
        string subject,
        string body,
        Guid? draftMessageId = null,
        Guid? parentMessageId = null)
    {
        await Initialization;

        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var authenticatedUserId) ||
            authenticatedUserId != senderId)
        {
            throw new InvalidOperationException("Se requiere una sesión autenticada del remitente.");
        }

        var response = await Client.Rpc("save_draft", new
        {
            p_sender_id = senderId,
            p_recipient_email = destinatarioEmail.Trim(),
            p_subject = subject,
            p_body = body,
            p_draft_message_id = draftMessageId,
            p_parent_message_id = parentMessageId
        });

        var responseContent = response.Content?.Trim().Trim('"');
        if (!Guid.TryParse(responseContent, out var savedMessageId))
        {
            throw new InvalidOperationException("Supabase no devolvió el ID del borrador guardado.");
        }

        return savedMessageId;
    }
}
