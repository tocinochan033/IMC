using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System.Text.Json.Serialization;

namespace IMC_.Models;

[Table("team_members")]
public class TeamMember : BaseModel
{
    [PrimaryKey("team_id", false)]
    [Column("team_id")]
    public Guid TeamId { get; set; }

    [PrimaryKey("user_id", false)]
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("joined_at", JsonIgnoreCondition.Never, true, false)]
    public DateTimeOffset JoinedAt { get; set; }
}
