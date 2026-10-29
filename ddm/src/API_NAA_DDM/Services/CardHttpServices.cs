using API_NAA_DDM.Dtos;
using API_NAA_DDM.Interfaces;
using ResponseModel;
using System.Text.Json;
using static ResponseModel.ResponseMapper;

namespace API_NAA_DDM.Services;

/// <summary>
/// Forwards NFC card login and binding requests to API_NAA, the same way
/// NaaHttpServices forwards the password login and history requests.
/// </summary>
public class CardHttpServices : ICardHttpServices
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly ILogger<CardHttpServices> _logger;

    public CardHttpServices(HttpClient client, ILogger<CardHttpServices> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<ResponseModel<LoginResponseDto>> AuthenticateByCardAsync(CardLoginRequestDto dto)
    {
        return PostToNaaAsync<CardLoginRequestDto, LoginResponseDto>("/AuthenticateByCard", dto);
    }

    public Task<ResponseModel<CardInfoDto>> GetCardsAsync(CardQueryDto dto)
    {
        return PostToNaaAsync<CardQueryDto, CardInfoDto>("/GetCards", dto);
    }

    public Task<ResponseModel<CardInfoDto>> BindCardAsync(CardBindDto dto)
    {
        return PostToNaaAsync<CardBindDto, CardInfoDto>("/BindCard", dto);
    }

    public Task<ResponseModel<string>> UnbindCardAsync(CardUnbindDto dto)
    {
        return PostToNaaAsync<CardUnbindDto, string>("/UnbindCard", dto);
    }

    private async Task<ResponseModel<TResponse>> PostToNaaAsync<TRequest, TResponse>(
        string path,
        TRequest dto)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(path, dto);

            if (!response.IsSuccessStatusCode)
                return GenerateErrorResponse<TResponse>($"NAA API returned {response.StatusCode}");

            var result = await response.Content.ReadFromJsonAsync<ResponseModel<TResponse>>(JsonOptions);
            return result ?? GenerateErrorResponse<TResponse>("NAA API returned an empty response");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NAA API call failed: {Path}", path);
            return GenerateErrorResponse<TResponse>("NAA API request failed");
        }
    }
}
