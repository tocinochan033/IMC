using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace IMC_.Models;

[Table("messages")]
public class Message : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("sender_id")]
    public Guid SenderId { get; set; }

    [Column("subject")]
    public string Subject { get; set; } = null!;

    [Column("body")]
    public string Body { get; set; } = null!;

    [Column("parent_message_id")]
    public Guid? ParentMessageId { get; set; }

    [Column("draft_recipient_email")]
    public string? DraftRecipientEmail { get; set; }

    [Column("created_at", JsonIgnoreCondition.Never, true, false)]
    public DateTimeOffset CreatedAt { get; set; }
}
