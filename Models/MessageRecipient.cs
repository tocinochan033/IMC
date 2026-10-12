using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace IMC_.Models;

[Table("message_recipients")]
public class MessageRecipient : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("recipient_id")]
    public Guid RecipientId { get; set; }

    [Column("recipient_type")]
    public string RecipientType { get; set; } = null!;

    [Column("created_at", JsonIgnoreCondition.Never, true, false)]
    public DateTimeOffset CreatedAt { get; set; }
}
