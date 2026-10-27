using Supabase;
using MailboxEntry = IMC_.Models.MailboxEntry;
using Message = IMC_.Models.Message;
using Profile = IMC_.Models.Profile;
using TaskAssignee = IMC_.Models.TaskAssignee;
using Team = IMC_.Models.Team;
using TeamMember = IMC_.Models.TeamMember;
using TeamTask = IMC_.Models.TeamTask;
using UserLabel = IMC_.Models.UserLabel;
using Operator = Supabase.Postgrest.Constants.Operator;
using System.Text.Json;

namespace IMC_.Services;

public enum ResultadoAgregarMiembro
{
    Agregado,
    CuentaNoEncontrada,
    YaEsMiembro,
    // El equipo solo lo puede ampliar su creador (decisión de producto: solo el jefe delega).
    NoAutorizado
}

public enum ResultadoEliminarMiembro
{
    Eliminado,
    // Quien llama no es el creador del equipo.
    NoAutorizado,
    // La persona indicada no pertenece al equipo (o la fila ya no existe).
    NoEsMiembro,
    // El creador no puede expulsarse a sí mismo: el equipo nunca se quedaría sin jefe.
    NoEliminarJefe
}

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

    public async Task<List<Team>> ObtenerEquiposDelUsuario()
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("consultar los equipos");

        var membershipsResponse = await Client
            .From<TeamMember>()
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Get();

        var teamIds = membershipsResponse.Models
            .Select(membership => membership.TeamId)
            .Distinct()
            .ToList();

        if (teamIds.Count == 0)
        {
            return [];
        }

        var teamsResponse = await Client
            .From<Team>()
            .Where(team => teamIds.Contains(team.Id))
            .Get();

        return teamsResponse.Models;
    }

    public async Task<List<Profile>> ObtenerMiembrosDelEquipo(Guid teamId)
    {
        await Initialization;
        _ = ObtenerUsuarioAutenticadoId("consultar los miembros del equipo");

        var membershipsResponse = await Client
            .From<TeamMember>()
            .Filter("team_id", Operator.Equals, teamId.ToString())
            .Get();

        var memberIds = membershipsResponse.Models
            .Select(membership => membership.UserId)
            .Distinct()
            .ToList();

        if (memberIds.Count == 0)
        {
            return [];
        }

        var profilesResponse = await Client
            .From<Profile>()
            .Where(profile => memberIds.Contains(profile.Id))
            .Get();

        // Orden determinista por nombre visible (nombre completo o email si falta, sin
        // distinguir mayúsculas): es el MISMO criterio con el que la página pinta la lista de
        // miembros y los chips de asignados, y con el que calcula el responsable principal
        // (tasks.assignee_id). Así "el primero de la lista" siempre es la misma persona y las
        // tres vistas coinciden; sin este OrderBy el orden venía sin ORDER BY de la BD.
        return profilesResponse.Models
            .OrderBy(profile => string.IsNullOrWhiteSpace(profile.FullName) ? profile.Email : profile.FullName,
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Team> CrearEquipo(string nombre, string? descripcion = null)
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("crear un equipo");

        if (!await PuedeCrearEquipoAsync(userId))
        {
            throw new UnauthorizedAccessException(
                "Solo los perfiles con rol team_manager, project_manager o admin pueden crear equipos.");
        }

        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException("El nombre del equipo es obligatorio.", nameof(nombre));
        }

        var response = await Client
            .From<Team>()
            .Insert(new Team
            {
                // Team.Id lleva [Column("id")], así que el cliente lo serializa en el
                // INSERT: si no se fija aquí viaja Guid.Empty y la BD persiste ese id
                // (el siguiente equipo chocaría en la PK con 23505). Mismo patrón que
                // ChatService.EnviarMensajeAsync; TeamTask no necesita el Guid porque su
                // Id no lleva [Column] y lo genera la BD.
                Id = Guid.NewGuid(),
                Name = nombre,
                Description = descripcion,
                CreatedBy = userId
            });

        var team = response.Models.FirstOrDefault()
            ?? throw new InvalidOperationException("Supabase no devolvió el equipo creado.");

        return team;
    }

    /// <summary>
    /// Consulta pública de si el usuario autenticado puede crear un equipo. Delega en
    /// <see cref="PuedeCrearEquipoAsync(Guid)"/>, que aplica la única regla de roles
    /// autorizados para gestionar equipos (team_manager, project_manager o admin).
    /// </summary>
    public async Task<bool> PuedeCrearEquipoAsync()
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("consultar los permisos para crear equipos");

        return await PuedeCrearEquipoAsync(userId);
    }

    public async Task<string?> ObtenerRolUsuarioActualAsync()
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("consultar el rol del usuario actual");

        var profileResponse = await Client
            .From<Profile>()
            .Filter("id", Operator.Equals, userId.ToString())
            .Get();

        return profileResponse.Models.FirstOrDefault()?.Role;
    }

    /// <summary>
    /// Única regla de quién puede CREAR un equipo: tener rol team_manager, project_manager o
    /// admin, es decir el MISMO conjunto de roles (y el mismo comparador) que decide quién
    /// puede invitar a un equipo: <see cref="EsRolAutorizadoParaGestionarEquipos"/>. Así
    /// "roles autorizados para gestionar equipos" es una sola noción compartida por crear e
    /// invitar y las dos reglas no pueden divergir.
    /// </summary>
    private async Task<bool> PuedeCrearEquipoAsync(Guid userId)
    {
        var profileResponse = await Client
            .From<Profile>()
            .Filter("id", Operator.Equals, userId.ToString())
            .Get();

        return EsRolAutorizadoParaGestionarEquipos(profileResponse.Models.FirstOrDefault()?.Role);
    }

    /// <summary>
    /// Comprueba si el usuario autenticado es el creador (jefe) del equipo.
    /// Se lee <c>teams.created_by</c> en lugar de invocar la RPC <c>is_team_creator</c>:
    /// es la MISMA columna con la que la página Equipos pinta "Creador/Miembro", de modo que
    /// la autorización en backend y lo que muestra la UI no pueden divergir, y el servicio no
    /// depende de que la RPC siga existiendo. Si la RLS de teams no deja leer la fila (no se
    /// es miembro), la consulta vuelve vacía ⇒ false, que es exactamente el resultado correcto.
    /// </summary>
    public async Task<bool> EsJefeDelEquipoAsync(Guid teamId)
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("comprobar quién creó el equipo");
        return await EsJefeDelEquipoAsync(teamId, userId);
    }

    private async Task<bool> EsJefeDelEquipoAsync(Guid teamId, Guid userId)
    {
        var response = await Client
            .From<Team>()
            .Filter("id", Operator.Equals, teamId.ToString())
            .Get();

        var team = response.Models.FirstOrDefault();
        return team is not null && team.CreatedBy == userId;
    }

    /// <summary>
    /// Expulsa a una persona del equipo. Solo lo puede hacer el creador, nunca contra sí
    /// mismo (el equipo no puede quedarse sin jefe) y solo sobre quien realmente es miembro.
    /// Devuelve el motivo en vez de lanzar: la página lo traduce a un mensaje concreto.
    /// </summary>
    public async Task<ResultadoEliminarMiembro> EliminarMiembro(Guid teamId, Guid userId)
    {
        await Initialization;
        var usuarioActual = ObtenerUsuarioAutenticadoId("quitar miembros del equipo");

        if (userId == Guid.Empty || userId == usuarioActual)
        {
            return ResultadoEliminarMiembro.NoEliminarJefe;
        }

        var teamResponse = await Client
            .From<Team>()
            .Filter("id", Operator.Equals, teamId.ToString())
            .Get();

        var team = teamResponse.Models.FirstOrDefault();
        if (team is null || team.CreatedBy != usuarioActual)
        {
            return ResultadoEliminarMiembro.NoAutorizado;
        }

        if (team.CreatedBy == userId)
        {
            // Defensa adicional: aunque el llamante no fuera el creador, nadie puede quitar al creador.
            return ResultadoEliminarMiembro.NoEliminarJefe;
        }

        var membershipResponse = await Client
            .From<TeamMember>()
            .Filter("team_id", Operator.Equals, teamId.ToString())
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Get();

        if (membershipResponse.Models.Count == 0)
        {
            return ResultadoEliminarMiembro.NoEsMiembro;
        }

        var deleteResponse = await Client
            .From<TeamMember>()
            .Filter("team_id", Operator.Equals, teamId.ToString())
            .Filter("user_id", Operator.Equals, userId.ToString())
            .Delete();

        // Mismo criterio honesto que en el resto del módulo: lo que decide es lo que la BD confirmó.
        return deleteResponse.Models.Count > 0
            ? ResultadoEliminarMiembro.Eliminado
            : ResultadoEliminarMiembro.NoEsMiembro;
    }

    /// <summary>
    /// Única regla de quién puede invitar a un equipo: ser el creador (teams.created_by, la
    /// MISMA columna con la que la página pinta "Creador/Miembro" y con la que se autoriza
    /// agregar/quitar miembros) Y tener rol team_manager, project_manager o admin. Es la
    /// intersección de lo que antes comprobaban por separado la página (rol) y este servicio
    /// (creador), es decir la política más restrictiva de las dos: así la UI y el backend no
    /// pueden divergir. El rol exigido es el MISMO conjunto que la regla de CREAR equipo
    /// (PuedeCrearEquipoAsync): team_manager, project_manager o admin, evaluado por
    /// <see cref="EsRolAutorizadoParaGestionarEquipos"/>; lo único que añade esta regla es la
    /// condición de ser el creador del equipo.
    /// </summary>
    public async Task<bool> PuedeInvitarAlEquipoAsync(Guid teamId)
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("comprobar los permisos para invitar al equipo");
        return await PuedeInvitarAlEquipoAsync(teamId, userId);
    }

    private async Task<bool> PuedeInvitarAlEquipoAsync(Guid teamId, Guid userId)
    {
        if (!await EsJefeDelEquipoAsync(teamId, userId))
        {
            return false;
        }

        return EsRolAutorizadoParaGestionarEquipos(await ObtenerRolUsuarioActualAsync());
    }

    // Única noción de "rol autorizado para gestionar equipos" (crear un equipo o ampliar uno
    // existente): la MISMA lista y el MISMO comparador que regían en la UI antes de
    // centralizar las reglas aquí, así crear e invitar comparten una sola definición.
    private static bool EsRolAutorizadoParaGestionarEquipos(string? role) =>
        string.Equals(role?.Trim(), "team_manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role?.Trim(), "project_manager", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role?.Trim(), "admin", StringComparison.OrdinalIgnoreCase);

    public async Task<ResultadoAgregarMiembro> AgregarMiembro(Guid teamId, string email)
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("agregar miembros al equipo");

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("El email es obligatorio.", nameof(email));
        }

        // Defensa en profundidad: la RLS de team_members ya exige ser el creador, aquí se
        // comprueba ANTES la misma regla completa que consume la UI (creador + rol) para no
        // mandar una llamada que va a ser rechazada.
        if (!await PuedeInvitarAlEquipoAsync(teamId, userId))
        {
            return ResultadoAgregarMiembro.NoAutorizado;
        }

        var profileResponse = await Client
            .From<Profile>()
            .Filter("email", Operator.Equals, email)
            .Get();

        var profile = profileResponse.Models.FirstOrDefault();
        if (profile is null)
        {
            return ResultadoAgregarMiembro.CuentaNoEncontrada;
        }

        var existingMembership = await Client
            .From<TeamMember>()
            .Filter("team_id", Operator.Equals, teamId.ToString())
            .Filter("user_id", Operator.Equals, profile.Id.ToString())
            .Get();

        if (existingMembership.Models.Count > 0)
        {
            return ResultadoAgregarMiembro.YaEsMiembro;
        }

        try
        {
            await Client
                .From<TeamMember>()
                .Insert(new TeamMember
                {
                    TeamId = teamId,
                    UserId = profile.Id
                });
        }
        catch (Supabase.Postgrest.Exceptions.PostgrestException exception) when (EsViolacionDeUnicidad(exception))
        {
            // La membresía pudo crearse después de la consulta anterior.
            return ResultadoAgregarMiembro.YaEsMiembro;
        }

        return ResultadoAgregarMiembro.Agregado;
    }

    public async Task<List<TeamTask>> ObtenerTareas(Guid teamId)
    {
        await Initialization;
        _ = ObtenerUsuarioAutenticadoId("consultar las tareas");

        var response = await Client
            .From<TeamTask>()
            .Filter("team_id", Operator.Equals, teamId.ToString())
            .Get();

        return response.Models;
    }

    /// <summary>
    /// Devuelve el conjunto completo de asignados (tabla <c>task_assignees</c>) de las tareas
    /// indicadas, agrupado por tarea. Se pide en UNA sola consulta para poder pintar el kanban
    /// completo sin disparar una petición por tarjeta.
    /// Sintaxis en Supabase.Postgrest 8.3.1: existe <c>Operator.In</c>, pero su criterio hay
    /// que construirlo a mano como cadena PostgREST "in.(...)" (con GUIDs, escapado incluido).
    /// Se usa la traducción LINQ <c>Where(x =&gt; ids.Contains(x.TaskId))</c> del mismo cliente,
    /// que genera ese filtro ya escapado y es ya el patrón del repo (ObtenerMiembrosDelEquipo,
    /// ObtenerNombresRemitentesAsync...): misma query, un riesgo menos y código consistente.
    /// </summary>
    public async Task<Dictionary<Guid, List<Guid>>> ObtenerAsignadosAsync(IEnumerable<Guid> taskIds)
    {
        await Initialization;
        _ = ObtenerUsuarioAutenticadoId("consultar los asignados de las tareas");

        var ids = taskIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var response = await Client
            .From<TaskAssignee>()
            .Where(assignee => ids.Contains(assignee.TaskId))
            .Get();

        return response.Models
            .GroupBy(assignee => assignee.TaskId)
            .ToDictionary(grupo => grupo.Key, grupo => grupo.Select(assignee => assignee.UserId).Distinct().ToList());
    }

    /// <summary>
    /// Crea una tarea y registra sus asignados en <c>task_assignees</c>.
    /// Solo el creador del equipo puede hacerlo: se comprueba aquí antes de escribir para no
    /// mandar una llamada que la RLS rechazaría (la BD sigue siendo la última barrera).
    /// <paramref name="asignados"/> es el conjunto completo de personas; si se omite se
    /// conserva la firma clásica y solo queda asignado <paramref name="responsableId"/>.
    /// </summary>
    public async Task<TeamTask> CrearTarea(
        Guid teamId,
        string titulo,
        string? descripcion = null,
        Guid? responsableId = null,
        DateTimeOffset? venceEn = null,
        IEnumerable<Guid>? asignados = null)
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("crear tareas");

        if (string.IsNullOrWhiteSpace(titulo))
        {
            throw new ArgumentException("El título de la tarea es obligatorio.", nameof(titulo));
        }

        if (!await EsJefeDelEquipoAsync(teamId, userId))
        {
            throw new UnauthorizedAccessException("Solo el creador del equipo puede crear tareas.");
        }

        var asignadosNormalizados = NormalizarAsignados(asignados, responsableId);

        var response = await Client
            .From<TeamTask>()
            .Insert(new TeamTask
            {
                TeamId = teamId,
                Title = titulo,
                Description = descripcion,
                Status = "todo",
                // El primer asignado del conjunto es el responsable principal (retrocompatibilidad).
                AssigneeId = asignadosNormalizados.Count > 0 ? asignadosNormalizados[0] : null,
                DueAt = venceEn
            });

        var task = response.Models.FirstOrDefault()
            ?? throw new InvalidOperationException("Supabase no devolvió la tarea creada.");

        if (asignadosNormalizados.Count > 0)
        {
            try
            {
                await SincronizarAsignadosAsync(task.Id, asignadosNormalizados);
            }
            catch (Exception)
            {
                // task_assignees se escribe después de la tarea (FK): si falla (tabla aún no
                // expuesta, RLS, caída transitoria) se retira la tarea recién creada para no
                // dejar un estado a medias que además provocaría tareas duplicadas al reintentar.
                try
                {
                    await EliminarTareaDirectaAsync(task.Id);
                }
                catch (Exception)
                {
                    // Mejor esfuerzo: si tampoco se puede retirar, manda el error original
                    // (el que explica el fallo de task_assignees) y la tarea quedaría sin asignados.
                }

                throw;
            }
        }

        return task;
    }

    /// <summary>
    /// Cambia el estado de una tarea. La BD solo lo permite al creador del equipo o a quien la
    /// tiene como <c>assignee_id</c> (trigger de <c>tasks</c>), así que la comprobación de
    /// asignación vive en la página (tiene la tarea y su asignación a mano y evita la consulta
    /// extra); el rechazo del trigger se captura ahí con un mensaje claro.
    /// </summary>
    public async Task ActualizarEstadoTarea(Guid taskId, string estado)
    {
        await Initialization;
        _ = ObtenerUsuarioAutenticadoId("actualizar tareas");

        if (estado is not ("todo" or "in_progress" or "done"))
        {
            throw new ArgumentException("El estado de la tarea no es válido.", nameof(estado));
        }

        await Client
            .From<TeamTask>()
            .Filter("id", Operator.Equals, taskId.ToString())
            .Set(task => task.Status, estado)
            .Update();
    }

    /// <summary>
    /// Actualiza los datos de una tarea y reescribe sus asignados en <c>task_assignees</c>.
    /// <paramref name="teamId"/> hace doble condición: sirve para autorizar (ser el creador)
    /// y se añade al WHERE para que un id de equipo incorrecto no pueda tocar tareas de otro.
    /// </summary>
    public async Task ActualizarTarea(
        Guid teamId,
        Guid taskId,
        string titulo,
        string? descripcion,
        Guid? responsableId,
        DateTimeOffset? venceEn,
        IEnumerable<Guid>? asignados = null)
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("actualizar tareas");

        if (string.IsNullOrWhiteSpace(titulo))
        {
            throw new ArgumentException("El título de la tarea es obligatorio.", nameof(titulo));
        }

        if (!await EsJefeDelEquipoAsync(teamId, userId))
        {
            throw new UnauthorizedAccessException("Solo el creador del equipo puede editar tareas.");
        }

        var asignadosNormalizados = NormalizarAsignados(asignados, responsableId);

        // Orden de escrituras pensado para que un fallo deje el estado anterior: primero se
        // leen los asignados actuales (base de la compensación), luego se reescribe
        // task_assignees y al final la tarea. Update() devuelve las filas que cumplieron el
        // WHERE: 0 significa que la tarea no existe o no pertenece al equipo, y avisar evita
        // pintar "guardado" sin guardar.
        var asignadosPrevios = (await ObtenerAsignadosAsync([taskId]))
            .TryGetValue(taskId, out var previos) ? previos : [];

        try
        {
            await SincronizarAsignadosAsync(taskId, asignadosNormalizados);

            var response = await Client
                .From<TeamTask>()
                .Filter("id", Operator.Equals, taskId.ToString())
                .Filter("team_id", Operator.Equals, teamId.ToString())
                .Set(task => task.Title, titulo)
                .Set(task => task.Description!, descripcion!)
                .Set(task => task.AssigneeId!, asignadosNormalizados.Count > 0 ? asignadosNormalizados[0] : null!)
                .Set(task => task.DueAt!, venceEn!)
                .Update();

            if (response.Models.Count == 0)
            {
                throw new InvalidOperationException("La tarea no existe o no pertenece a ese equipo.");
            }
        }
        catch (Exception)
        {
            // Compensación (mismo criterio que CrearTarea, que retira la tarea creada si
            // task_assignees falla): si algo falló después de tocar task_assignees, se
            // reescribe el conjunto anterior para no dejar la tarea con los asignados
            // vacíos o a medias.
            try
            {
                await SincronizarAsignadosAsync(taskId, asignadosPrevios);
            }
            catch (Exception)
            {
                // Mejor esfuerzo: si tampoco se puede restaurar, se propaga el error original.
            }

            throw;
        }
    }

    public async Task EliminarTarea(Guid teamId, Guid taskId)
    {
        await Initialization;
        var userId = ObtenerUsuarioAutenticadoId("eliminar tareas");

        if (!await EsJefeDelEquipoAsync(teamId, userId))
        {
            throw new UnauthorizedAccessException("Solo el creador del equipo puede eliminar tareas.");
        }

        await Client
            .From<TeamTask>()
            .Filter("id", Operator.Equals, taskId.ToString())
            .Filter("team_id", Operator.Equals, teamId.ToString())
            .Delete();
    }

    /// <summary>
    /// Devuelve el conjunto de asignados sin duplicados ni ids vacíos y con el responsable
    /// principal el primero: ese orden define quién queda en <c>tasks.assignee_id</c>.
    /// </summary>
    private static List<Guid> NormalizarAsignados(IEnumerable<Guid>? asignados, Guid? responsableId)
    {
        List<Guid> ids = responsableId is Guid principal ? [principal] : [];

        if (asignados is not null)
        {
            ids.AddRange(asignados);
        }

        return ids.Where(id => id != Guid.Empty).Distinct().ToList();
    }

    /// <summary>
    /// Reescribe las filas de <c>task_assignees</c> de una tarea: borra las previas y
    /// reinserta las actuales. Se eligió "borrar todo y reinsertar" frente a un diff porque
    /// el conjunto suele tener 1-3 filas (coste irrelevante), así se refleja también una
    /// baja y no puede quedar ninguna fila huérfana. Solo lo llama el jefe (comprobado antes).
    /// </summary>
    private async Task SincronizarAsignadosAsync(Guid taskId, IReadOnlyCollection<Guid> asignados)
    {
        await Client
            .From<TaskAssignee>()
            .Filter("task_id", Operator.Equals, taskId.ToString())
            .Delete();

        if (asignados.Count == 0)
        {
            return;
        }

        await Client
            .From<TaskAssignee>()
            .Insert(asignados
                .Select(userId => new TaskAssignee { TaskId = taskId, UserId = userId })
                .ToList());
    }

    // Borra sin autorizar (solo se usa para deshacer una tarea creada por el propio servicio).
    private Task EliminarTareaDirectaAsync(Guid taskId) =>
        Client
            .From<TeamTask>()
            .Filter("id", Operator.Equals, taskId.ToString())
            .Delete();

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

    private Guid ObtenerUsuarioAutenticadoId(string operacion)
    {
        if (!Guid.TryParse(Client.Auth.CurrentSession?.User?.Id, out var userId))
        {
            throw new InvalidOperationException($"Se requiere una sesión autenticada para {operacion}.");
        }

        return userId;
    }

    private static bool EsViolacionDeUnicidad(Supabase.Postgrest.Exceptions.PostgrestException exception)
    {
        if (string.IsNullOrWhiteSpace(exception.Content))
        {
            return false;
        }

        try
        {
            using var content = JsonDocument.Parse(exception.Content);
            return content.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() == "23505";
        }
        catch (JsonException)
        {
            return false;
        }
    }

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
