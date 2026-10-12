using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace IMC_.Models;

[Table("attachments")]
public class Attachment : BaseModel
{
    [PrimaryKey("id", false)]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("message_id")]
    public Guid MessageId { get; set; }

    [Column("file_name")]
    public string FileName { get; set; } = null!;

    [Column("file_path")]
    public string FilePath { get; set; } = null!;

    [Column("file_size")]
    public long FileSize { get; set; }

    [Column("created_at", JsonIgnoreCondition.Never, true, false)]
    public DateTimeOffset CreatedAt { get; set; }
}
