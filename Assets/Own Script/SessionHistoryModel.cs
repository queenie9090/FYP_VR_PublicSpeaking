using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using System;

[Table("session_history")]
public class SessionHistoryModel : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; }

    [Column("user_id")]
    public string UserId { get; set; }

    [Column("scenario_name")]
    public string ScenarioName { get; set; }

    [Column("input_mode")]
    public string InputMode { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}