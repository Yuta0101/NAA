namespace API_NAA_DDM.Dtos;

public sealed class CardLoginRequestDto
{
    public string? CardUid { get; set; }
}

public sealed class CardQueryDto
{
    public string? Account { get; set; }
}

public sealed class CardBindDto
{
    public string? Account { get; set; }
    public string? CardUid { get; set; }
}

public sealed class CardUnbindDto
{
    public string? Account { get; set; }
    public string? CardId { get; set; }
}

public sealed class CardInfoDto
{
    public string CardId { get; set; } = null!;
    public string MaskedUid { get; set; } = null!;
    public DateTime? BoundAt { get; set; }
}
