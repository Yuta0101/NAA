using API_NAA.Dtos.Input.Create;
using API_NAA.Dtos.Input.Query;
using API_NAA.Dtos.Input.Update;
using API_NAA.Dtos.Output.Origin;
using API_NAA.Interfaces;
using Microsoft.AspNetCore.Mvc;
using ResponseModel;

namespace API_NAA.EndPoints;

public class CardEndPoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/AuthenticateByCard", async ([FromServices] ICardServices cardService, CardLoginRequestDto dto) =>
            Results.Ok(await cardService.AuthenticateByCardAsync(dto)))
            .WithName("AuthenticateByCard")
            .WithTags("身分驗證")
            .WithSummary("以 NFC 卡片 UID 查詢已綁定的帳號")
            .Produces<ResponseModel<LoginResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetCards", async ([FromServices] ICardServices cardService, CardQueryDto dto) =>
            Results.Ok(await cardService.GetCardsAsync(dto)))
            .WithName("GetCards")
            .WithTags("卡片綁定")
            .WithSummary("取得指定帳號目前綁定的卡片")
            .Produces<ResponseModel<CardInfoDto>>(StatusCodes.Status200OK);

        app.MapPost("/BindCard", async ([FromServices] ICardServices cardService, CardBindDto dto) =>
            Results.Ok(await cardService.BindCardAsync(dto)))
            .WithName("BindCard")
            .WithTags("卡片綁定")
            .WithSummary("將 NFC 卡片綁定到指定帳號")
            .Produces<ResponseModel<CardInfoDto>>(StatusCodes.Status200OK);

        app.MapPost("/UnbindCard", async ([FromServices] ICardServices cardService, CardUnbindDto dto) =>
            Results.Ok(await cardService.UnbindCardAsync(dto)))
            .WithName("UnbindCard")
            .WithTags("卡片綁定")
            .WithSummary("解除指定帳號的卡片綁定")
            .Produces<ResponseModel<string>>(StatusCodes.Status200OK);
    }
}
