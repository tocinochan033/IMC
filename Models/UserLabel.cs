using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IMC_.Models;

[Table("user_labels")]
public class UserLabel : BaseModel
{
    [PrimaryKey("id", false)]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("label")]
    public string Label { get; set; } = null!;
}
