using API_NAA.Dtos.Input.Create;
using API_NAA.Dtos.Input.Query;
using API_NAA.Dtos.Input.Update;
using API_NAA.Dtos.Output.Origin;
using ResponseModel;

namespace API_NAA.Interfaces;

public interface ICardServices
{
    Task<ResponseModel<LoginResponseDto>> AuthenticateByCardAsync(CardLoginRequestDto dto);
    Task<ResponseModel<CardInfoDto>> GetCardsAsync(CardQueryDto dto);
    Task<ResponseModel<CardInfoDto>> BindCardAsync(CardBindDto dto);
    Task<ResponseModel<string>> UnbindCardAsync(CardUnbindDto dto);
}
