using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace IMC_.Models;

[Table("profiles")]
public class Profile : BaseModel
{
    [PrimaryKey("id")]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("email")]
    public string Email { get; set; } = null!;

    [Column("full_name")]
    public string FullName { get; set; } = null!;

    [Column("role")]
    public string Role { get; set; } = null!;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
