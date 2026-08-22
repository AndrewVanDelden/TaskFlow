using System.ComponentModel.DataAnnotations;

namespace TaskFlow.Api.DTOs;

public sealed class ParseUrlDto
{
    [Required]
    public string Url { get; set; } = string.Empty;
}
