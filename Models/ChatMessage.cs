using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace IMC_.Models;

[Table("chat_messages")]
public class ChatMessage : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("sender_id")]
    public Guid SenderId { get; set; }

    [Column("recipient_id")]
    public Guid RecipientId { get; set; }

    [Column("conversation_id")]
    public string ConversationId { get; set; } = null!;

    [Column("subject")]
    public string Subject { get; set; } = string.Empty;

    [Column("body")]
    public string Body { get; set; } = null!;

    [Column("parent_message_id")]
    public Guid? ParentMessageId { get; set; }

    [Column("is_read")]
    public bool IsRead { get; set; }

    [Column("is_delivered")]
    public bool IsDelivered { get; set; }

    [Column("is_edited")]
    public bool IsEdited { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    // En el ctor de ColumnAttribute (Supabase.Postgrest 8.3.1, verificado por reflexión) el
    // orden es (columnName, nullValueHandling, ignoreOnInsert, ignoreOnUpdate): el "true" es
    // ignorar created_at en INSERT (lo genera la BD), NO una marca de clave primaria; la PK
    // se declara aparte con [PrimaryKey("id", false)]. Se serializa siempre (JsonIgnoreCondition.Never)
    // porque el INSERT no lo envía pero el hilo y los sellos de tiempo sí lo necesitan leer.
    [Column("created_at", JsonIgnoreCondition.Never, true, false)]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }
}
