using IMC_.Models;
using Supabase.Postgrest.Interfaces;
using Operator = Supabase.Postgrest.Constants.Operator;
using Ordering = Supabase.Postgrest.Constants.Ordering;
using QueryFilter = Supabase.Postgrest.QueryFilter;

namespace IMC_.Services;

public enum ResultadoEnvioChat
{
    Enviado,
    DestinatarioNoValido,
    // Quien escribe no pertenece al equipo: no hay grupo al que enviar.
    NoMiembro
}

public enum ResultadoMarcarLeidos
{
    Marcado,
    SinPermiso,
    SinCambios
}

/// <summary>
/// Acceso a la tabla real <c>chat_messages</c> para el módulo de chat interno.
/// Todas las consultas se ejecutan con la sesión autenticada del usuario actual.
/// </summary>
public class ChatService
{
    private readonly SupabaseService supabase;

    // Ventana de trabajo de mensajes: suficiente para el volumen actual de un turno de
    // trabajo sin cargar tablas enteras. Implicación (documentada a propósito): el badge de
    // no leídos y el "último mensaje" de cada conversación se calculan SOLO sobre esta
    // ventana, así que un no leído más antiguo que los últimos N mensajes no se contaría en
    // el badge. Con 300 y el volumen actual hay margen de sobra; si el uso crece, subir este
    // número (no requiere tocar la UI) o pasar a un conteo agregado en BD.
    private const int MensajesLimite = 300;

    // Tope de contactos para poblar la lista del chat (y el badge del FAB en el arranque):
    // evita descargar la tabla profiles completa cuando la organización crece.
    private const int ContactosLimite = 200;

    public ChatService(SupabaseService supabase)
    {
        this.supabase = supabase;
    }

    /// <summary>
    /// Se dispara cuando otra página (Equipos) pide abrir el chat en el grupo de un equipo.
    /// Patrón del repo: igual que <c>SupabaseService.EtiquetasActualizadas</c>, un evento C#
    /// del servicio scoped que el <c>ChatPanel</c> escucha; así no hace falta JavaScript ni
    /// pasar parámetros por query string entre la página y el layout.
    /// </summary>
    public event Action<Guid, string>? ConversacionDeGrupoSolicitada;

    /// <summary>
    /// Pide abrir el panel de chat enfocado en el grupo del equipo. Es sincrónico a propósito:
    /// solo notifica; quien decide cómo abrirlo (y captura los fallos) es el ChatPanel.
    /// </summary>
    public void AbrirConversacionDeGrupo(Guid teamId, string nombreEquipo) =>
        ConversacionDeGrupoSolicitada?.Invoke(teamId, nombreEquipo);

    /// <summary>
    /// Contrato del <c>conversation_id</c> de un equipo: "team_" + id del equipo, en la MISMA
    /// tabla chat_messages (patrón de una fila por destinatario) y sin mezclar con los DM,
    /// cuyo conversation_id es "&lt;uuidMenor&gt;_&lt;uuidMayor&gt;".
    /// </summary>
    public static string ConversationIdDeGrupo(Guid teamId) => $"team_{teamId}";

    /// <summary>
    /// Devuelve todos los perfiles excepto el del usuario autenticado (los contactos del chat).
    /// Pide solo las columnas que consume el panel (id, email, full_name, role) y acota el
    /// resultado: el chat no necesita la tabla completa para poblar la lista de contactos.
    /// </summary>
    public async Task<List<Profile>> ObtenerContactosAsync()
    {
        await supabase.Initialization;
        var userId = ObtenerUsuarioAutenticadoId("consultar los contactos del chat");

        var response = await supabase.Client
            .From<Profile>()
            .Select("id,email,full_name,role")
            .Limit(ContactosLimite)
            .Get();

        return response.Models
            .Where(profile => profile.Id != userId)
            .ToList();
    }

    /// <summary>
    /// Devuelve los mensajes en los que participa el usuario autenticado (enviados o
    /// recibidos), sin los eliminados, en una sola consulta. Es la única fuente de datos
    /// que consume el panel: lista de conversaciones y hilo de mensajes.
    /// Grupos: NO hace falta una segunda query. Los mensajes de grupo viven en la MISMA
    /// tabla con el patrón de buzón (una fila por destinatario), así que esta query ya los
    /// cubre — mis copias de grupo entran por <c>sender_id = yo</c> (son las que necesito
    /// para los recibos ✓✓) y las copias que otros me dirigen entran por
    /// <c>recipient_id = yo</c> (y SOLO esa, que es lo que evita duplicados en los mensajes
    /// ajenos). Añadir un segundo GET solo duplicaría filas y costaría otro viaje.
    /// Semántica del límite: se piden los N más recientes de TODO (DM + grupos) por
    /// created_at descendente, es decir, lo más nuevo primero, igual que antes. Cambio
    /// documentado: una copia propia de grupo ocupa una fila de la ventana por destinatario,
    /// así que con grupos la ventana cubre menos "mensajes únicos"; con MensajesLimite = 300
    /// sigue habiendo margen y, si crece el uso, subir la constante no toca la UI.
    /// </summary>
    public async Task<List<ChatMessage>> ObtenerMensajesAsync()
    {
        await supabase.Initialization;
        var userId = ObtenerUsuarioAutenticadoId("consultar los mensajes del chat");

        // Una sola query con OR en vez de dos GET (enviados + recibidos): mismo resultado
        // con la mitad de viajes a Supabase. El GUID del filtro es SIEMPRE el del usuario
        // autenticado (ObtenerUsuarioAutenticadoId), nunca entrada del usuario, y viaja como
        // criterio tipado de PostgREST, así que no hay riesgo de inyección de query.
        var response = await supabase.Client
            .From<ChatMessage>()
            .Or(new List<IPostgrestQueryFilter>
            {
                new QueryFilter("sender_id", Operator.Equals, userId.ToString()),
                new QueryFilter("recipient_id", Operator.Equals, userId.ToString())
            })
            .Filter("is_deleted", Operator.Equals, "false")
            .Order("created_at", Ordering.Descending)
            .Limit(MensajesLimite)
            .Get();

        // La query trae los N más recientes; en cliente se ordena ascendente para pintar el
        // hilo de abajo hacia arriba como hasta ahora.
        return response.Models
            .OrderBy(message => message.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Inserta un mensaje en <c>chat_messages</c>. El <c>conversation_id</c> se calcula
    /// con el contrato exacto de la tabla: "&lt;uuidMenor&gt;_&lt;uuidMayor&gt;" lexicográfico.
    /// </summary>
    public async Task<ResultadoEnvioChat> EnviarMensajeAsync(Guid destinatarioId, string texto)
    {
        await supabase.Initialization;
        var userId = ObtenerUsuarioAutenticadoId("enviar mensajes del chat");

        if (destinatarioId == userId || string.IsNullOrWhiteSpace(texto))
        {
            return ResultadoEnvioChat.DestinatarioNoValido;
        }

        var destinatarioResponse = await supabase.Client
            .From<Profile>()
            .Filter("id", Operator.Equals, destinatarioId.ToString())
            .Get();

        if (destinatarioResponse.Models.Count == 0)
        {
            return ResultadoEnvioChat.DestinatarioNoValido;
        }

        await supabase.Client
            .From<ChatMessage>()
            .Insert(new ChatMessage
            {
                Id = Guid.NewGuid(),
                SenderId = userId,
                RecipientId = destinatarioId,
                ConversationId = CalcularConversationId(userId, destinatarioId),
                Subject = string.Empty,
                Body = texto.Trim(),
                CreatedAt = DateTimeOffset.UtcNow
            });

        return ResultadoEnvioChat.Enviado;
    }

    /// <summary>
    /// Envía un mensaje al grupo de chat de un equipo: UNA fila por miembro en
    /// <c>chat_messages</c> (incluida la copia propia), todas con el mismo
    /// <c>parent_message_id</c> — el Guid de correlación que luego permite deduplicar el hilo
    /// y calcular los recibos del grupo.
    /// Devuelve <see cref="ResultadoEnvioChat.NoMiembro"/> si quien escribe no pertenece al
    /// equipo: no se envía nada (tampoco a medias, porque la validación es previa al INSERT).
    /// </summary>
    public async Task<ResultadoEnvioChat> EnviarMensajeAGrupoAsync(Guid teamId, string texto)
    {
        await supabase.Initialization;
        var userId = ObtenerUsuarioAutenticadoId("enviar mensajes al grupo");

        if (teamId == Guid.Empty || string.IsNullOrWhiteSpace(texto))
        {
            return ResultadoEnvioChat.DestinatarioNoValido;
        }

        // Una sola query que cumple dos papeles: lista de destinatarios y validación de
        // membresía (si no estoy en la fila, no es mi grupo). La RLS de team_members ya
        // limita la lectura a los equipos a los que pertenezco, así que un intruso vería su
        // propia fila ausente ⇒ NoMiembro.
        var membershipsResponse = await supabase.Client
            .From<TeamMember>()
            .Filter("team_id", Operator.Equals, teamId.ToString())
            .Get();

        var miembros = membershipsResponse.Models
            .Select(membership => membership.UserId)
            .Distinct()
            .ToList();

        if (!miembros.Contains(userId))
        {
            return ResultadoEnvioChat.NoMiembro;
        }

        if (miembros.Count == 0)
        {
            return ResultadoEnvioChat.DestinatarioNoValido;
        }

        var mensajeId = Guid.NewGuid();
        var conversationId = ConversationIdDeGrupo(teamId);

        var copias = miembros
            .Select(destinatario => new ChatMessage
            {
                Id = Guid.NewGuid(),
                SenderId = userId,
                RecipientId = destinatario,
                ConversationId = conversationId,
                Subject = string.Empty,
                Body = texto.Trim(),
                ParentMessageId = mensajeId,
                // MI copia nace leída/entregada: soy quien la escribe, así el badge de no
                // leídos (recipient_id = yo && !is_read) y el marcado del hilo jamás la
                // cuentan ni la vuelven a "leer" por error. Las de los demás empiezan en
                // false: solo ellas reflejan lo que el destinatario confirma en BD.
                IsRead = destinatario == userId,
                IsDelivered = destinatario == userId
            })
            .ToList();

        // Insert(ICollection<T>) del cliente 8.3.1 => un POST con array de PostgREST:
        // TODO el mensaje se guarda en 1 sola petición (si no, serían N).
        await supabase.Client.From<ChatMessage>().Insert(copias);

        return ResultadoEnvioChat.Enviado;
    }

    /// <summary>
    /// Marca como entregados y leídos los mensajes que <paramref name="contactoId"/>
    /// envió al usuario autenticado en su conversación (recibos ✓✓).
    /// Devuelve lo que la BD confirmó: con RLS un <c>UPDATE</c> responde 200 aunque no
    /// afecte ninguna fila, así que se leen las filas devueltas por <c>return=representation</c>.
    /// </summary>
    public async Task<ResultadoMarcarLeidos> MarcarLeidosAsync(Guid contactoId)
    {
        await supabase.Initialization;
        var userId = ObtenerUsuarioAutenticadoId("marcar mensajes como leídos");
        var conversacionId = CalcularConversationId(userId, contactoId);

        // Un solo UPDATE: en el caso normal (RLS permite) no hay consulta previa.
        var response = await supabase.Client
            .From<ChatMessage>()
            .Filter("conversation_id", Operator.Equals, conversacionId)
            .Filter("sender_id", Operator.Equals, contactoId.ToString())
            .Filter("recipient_id", Operator.Equals, userId.ToString())
            .Filter("is_read", Operator.Equals, "false")
            .Set(message => message.IsDelivered, true)
            .Set(message => message.IsRead, true)
            .Update();

        // Update() usa return=representation por defecto: Models contiene las filas
        // realmente afectadas.
        if (response.Models.Count > 0)
        {
            return ResultadoMarcarLeidos.Marcado;
        }

        // 0 filas con HTTP 2xx es ambiguo: "no había pendientes" y "RLS filtró el UPDATE"
        // responden igual, así que SOLO en este caso (la rara vez) se consulta el estado
        // previo para distinguirlos sin penalizar el camino normal con una query extra.
        var pendientes = await supabase.Client
            .From<ChatMessage>()
            .Filter("conversation_id", Operator.Equals, conversacionId)
            .Filter("sender_id", Operator.Equals, contactoId.ToString())
            .Filter("recipient_id", Operator.Equals, userId.ToString())
            .Filter("is_read", Operator.Equals, "false")
            .Get();

        return pendientes.Models.Count > 0
            ? ResultadoMarcarLeidos.SinPermiso
            : ResultadoMarcarLeidos.SinCambios;
    }

    /// <summary>
    /// Equivalente en grupo de "marcar leídos": marca como leídos TODOS los mensajes de la
    /// conversación de equipo que esperan mi copia: filas con mi <c>conversation_id</c> de
    /// equipo, <c>recipient_id = yo</c> e <c>is_read = false</c>. La política actual de
    /// chat_messages lo permite (actualizo mi propia copia) y NO toca las copias de los
    /// demás, de modo que sus recibos siguen siendo honestos. Se conserva la misma
    /// semántica que en los DM: solo se pinta lo que la BD confirma y, si responde 200 con 0
    /// filas, se distingue "sin permiso" de "sin cambios" con una query de diagnóstico y hay
    /// backoff de 60 s.
    /// </summary>
    public async Task<ResultadoMarcarLeidos> MarcarLeidosDeGrupoAsync(Guid teamId)
    {
        await supabase.Initialization;
        var userId = ObtenerUsuarioAutenticadoId("marcar mensajes del grupo como leídos");
        var conversationId = ConversationIdDeGrupo(teamId);

        var response = await supabase.Client
            .From<ChatMessage>()
            .Filter("conversation_id", Operator.Equals, conversationId)
            .Filter("recipient_id", Operator.Equals, userId.ToString())
            .Filter("is_read", Operator.Equals, "false")
            .Set(message => message.IsDelivered, true)
            .Set(message => message.IsRead, true)
            .Update();

        if (response.Models.Count > 0)
        {
            return ResultadoMarcarLeidos.Marcado;
        }

        var pendientes = await supabase.Client
            .From<ChatMessage>()
            .Filter("conversation_id", Operator.Equals, conversationId)
            .Filter("recipient_id", Operator.Equals, userId.ToString())
            .Filter("is_read", Operator.Equals, "false")
            .Get();

        return pendientes.Models.Count > 0
            ? ResultadoMarcarLeidos.SinPermiso
            : ResultadoMarcarLeidos.SinCambios;
    }

    /// <summary>
    /// Contrato exacto de la columna <c>conversation_id</c>:
    /// "&lt;uuidMenor&gt;_&lt;uuidMayor&gt;" en orden lexicográfico de sus representaciones texto.
    /// </summary>
    public static string CalcularConversationId(Guid a, Guid b)
    {
        var primero = a.ToString("D");
        var segundo = b.ToString("D");

        return string.CompareOrdinal(primero, segundo) <= 0
            ? $"{primero}_{segundo}"
            : $"{segundo}_{primero}";
    }

    private Guid ObtenerUsuarioAutenticadoId(string operacion)
    {
        if (!Guid.TryParse(supabase.Client.Auth.CurrentSession?.User?.Id, out var userId))
        {
            throw new InvalidOperationException($"Se requiere una sesión autenticada para {operacion}.");
        }

        return userId;
    }
}
