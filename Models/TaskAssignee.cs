using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace IMC_.Models;

// Asignación múltiple de tareas: una fila por (tarea, persona). tasks.assignee_id se
// conserva como asignado "principal" para no romper lo que ya existe (retrocompatibilidad);
// esta tabla es la fuente de verdad del conjunto completo de asignados.
[Table("task_assignees")]
public class TaskAssignee : BaseModel
{
    [PrimaryKey("task_id", false)]
    [Column("task_id")]
    public Guid TaskId { get; set; }

    [PrimaryKey("user_id", false)]
    [Column("user_id")]
    public Guid UserId { get; set; }

    // Mismo patrón que TeamMember.JoinedAt: created_at lo genera la BD en el INSERT
    // (ignoreOnInsert = true) pero se serializa siempre para poder leerlo si hace falta.
    [Column("created_at", JsonIgnoreCondition.Never, true, false)]
    public DateTimeOffset CreatedAt { get; set; }
}
