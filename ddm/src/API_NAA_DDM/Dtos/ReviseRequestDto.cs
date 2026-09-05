using System.ComponentModel.DataAnnotations;

namespace API_NAA_DDM.Dtos;

public class ReviseRequestDto
{
    [Required]
    public string ConversationId { get; set; } = string.Empty;

    public string? ChatTitle { get; set; }

    [Required]
    public string InputText { get; set; } = string.Empty;

    [Required]
    public string Account { get; set; } = string.Empty;

    public string EmployeeId { get; set; } = string.Empty;

    public string OriginCode { get; set; } = "DDM";
}
