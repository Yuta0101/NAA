namespace API_NAA.Dtos.Output.Origin;

public sealed class CardInfoDto
{
    public string CardId { get; set; } = null!;

    /// <summary>
    /// Only the last four characters are returned so the full UID never
    /// reaches the browser after it has been bound.
    /// </summary>
    public string MaskedUid { get; set; } = null!;

    public DateTime? BoundAt { get; set; }
}
