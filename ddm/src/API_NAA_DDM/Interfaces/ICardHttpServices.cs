using API_NAA_DDM.Dtos;
using ResponseModel;

namespace API_NAA_DDM.Interfaces;

public interface ICardHttpServices
{
    Task<ResponseModel<LoginResponseDto>> AuthenticateByCardAsync(CardLoginRequestDto dto);
    Task<ResponseModel<CardInfoDto>> GetCardsAsync(CardQueryDto dto);
    Task<ResponseModel<CardInfoDto>> BindCardAsync(CardBindDto dto);
    Task<ResponseModel<string>> UnbindCardAsync(CardUnbindDto dto);
}
