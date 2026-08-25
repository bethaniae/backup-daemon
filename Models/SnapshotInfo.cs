using System.Text.Json.Serialization;

namespace BackupManager.Models;

public class SnapshotInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("short_id")]
    public string ShortId { get; set; } = "";

    [JsonPropertyName("time")]
    public DateTime Time { get; set; }

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("paths")]
    public List<string> Paths { get; set; } = new();

    [JsonPropertyName("tree")]
    public string Tree { get; set; } = "";

    [JsonPropertyName("summary")]
    public SnapshotSummary? Summary { get; set; }
}

public class SnapshotSummary
{
    [JsonPropertyName("total_bytes_processed")]
    public long TotalBytesProcessed { get; set; }

    [JsonPropertyName("total_bytes_added")]
    public long TotalBytesAdded { get; set; }
}
