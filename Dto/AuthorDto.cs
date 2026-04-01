using System.Text.Json.Serialization;

namespace JellyfinBookReader.Dto;

public class AuthorDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("bookCount")]
    public int BookCount { get; set; }
}