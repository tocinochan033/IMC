using IMC_.Models;

namespace IMC_.Services;

public class FakeDataService
{
    private static readonly Guid UsuarioId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static readonly List<Message> Mensajes =
    [
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            SenderId = Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Subject = "Bienvenida al equipo",
            Body = "Te damos la bienvenida. Esta semana tendremos una reunión para presentarte al equipo.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            SenderId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Subject = "Reunión de proyecto",
            Body = "¿Podemos reunirnos el jueves para revisar los avances del proyecto?",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-2)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            SenderId = Guid.Parse("20000000-0000-0000-0000-000000000003"),
            Subject = "Factura de octubre",
            Body = "Adjunto encontrarás la factura correspondiente al mes de octubre.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
            SenderId = Guid.Parse("20000000-0000-0000-0000-000000000004"),
            Subject = "Fotos del evento",
            Body = "Aquí tienes el enlace con las fotos del evento del fin de semana.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-4)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000005"),
            SenderId = Guid.Parse("20000000-0000-0000-0000-000000000005"),
            Subject = "Actualización de contraseña",
            Body = "Tu contraseña se actualizó correctamente. Si no fuiste tú, contáctanos.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-5)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000006"),
            SenderId = Guid.Parse("20000000-0000-0000-0000-000000000006"),
            Subject = "Plan para el viernes",
            Body = "¿Te gustaría almorzar con el equipo este viernes?",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-6)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000007"),
            SenderId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Subject = "Resumen de avances",
            Body = "Comparto el resumen de avances y las tareas pendientes de esta semana.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000008"),
            SenderId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Subject = "Confirmación de asistencia",
            Body = "Confirmo mi asistencia a la reunión del jueves. Gracias por la invitación.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-3)
        },
        new()
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000009"),
            SenderId = Guid.Parse("20000000-0000-0000-0000-000000000007"),
            Subject = "Promoción especial",
            Body = "Descubre nuestras ofertas especiales disponibles durante esta semana.",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7)
        }
    ];

    private static readonly List<MailboxEntry> Entradas =
    [
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000001"), Folder = "inbox", IsRead = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000002"), Folder = "inbox", IsRead = false, CreatedAt = DateTimeOffset.UtcNow.AddDays(-2) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000003"), Folder = "inbox", IsRead = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000004"), Folder = "inbox", IsRead = false, CreatedAt = DateTimeOffset.UtcNow.AddDays(-4) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000005"), Folder = "inbox", IsRead = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-5) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000006"), Folder = "inbox", IsRead = false, CreatedAt = DateTimeOffset.UtcNow.AddDays(-6) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000007"), Folder = "sent", IsRead = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000008"), Folder = "sent", IsRead = true, CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) },
        new() { Id = Guid.NewGuid(), UserId = UsuarioId, MessageId = Guid.Parse("10000000-0000-0000-0000-000000000009"), Folder = "trash", IsRead = false, CreatedAt = DateTimeOffset.UtcNow.AddDays(-7) }
    ];

    public IReadOnlyList<MailboxEntry> EntradasBandeja => Entradas;

    public List<Message> ObtenerBandejaEntrada() => ObtenerMensajesDeCarpeta("inbox");

    public List<Message> ObtenerEnviados() => ObtenerMensajesDeCarpeta("sent");

    public List<Message> ObtenerPapelera() => ObtenerMensajesDeCarpeta("trash");

    public Message? ObtenerMensajePorId(string id)
    {
        return Guid.TryParse(id, out var messageId)
            ? Mensajes.FirstOrDefault(mensaje => mensaje.Id == messageId)
            : null;
    }

    private static List<Message> ObtenerMensajesDeCarpeta(string carpeta)
    {
        var idsMensajes = Entradas
            .Where(entrada => entrada.Folder == carpeta)
            .Select(entrada => entrada.MessageId)
            .ToHashSet();

        return Mensajes
            .Where(mensaje => idsMensajes.Contains(mensaje.Id))
            .ToList();
    }
}
