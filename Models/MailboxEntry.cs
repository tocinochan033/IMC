using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace IMC_.Models;

[Table("mailbox_entries")]
public class MailboxEntry : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("folder")]
    public string Folder { get; set; } = null!;

    [Column("is_read")]
    public bool IsRead { get; set; }

    [Column("is_starred")]
    public bool IsStarred { get; set; }

    [Column("label")]
    public string? Label { get; set; }

    [Column("created_at", JsonIgnoreCondition.Never, true, false)]
    public DateTimeOffset CreatedAt { get; set; }
}
