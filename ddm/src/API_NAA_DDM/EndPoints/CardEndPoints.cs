using API_NAA_DDM.Dtos;
using API_NAA_DDM.Interfaces;
using Microsoft.AspNetCore.Mvc;
using ResponseModel;

namespace API_NAA_DDM.EndPoints;

public class CardEndPoints : INaaEndPoint
{
    public void NaaEndPoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/CardLogin", async ([FromServices] ICardHttpServices svc, CardLoginRequestDto dto) =>
            Results.Ok(await svc.AuthenticateByCardAsync(dto)))
            .WithName("CardLogin")
            .WithTags("身分驗證")
            .WithSummary("以 NFC 卡片 UID 登入")
            .Produces<ResponseModel<LoginResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetCards", async ([FromServices] ICardHttpServices svc, CardQueryDto dto) =>
            Results.Ok(await svc.GetCardsAsync(dto)))
            .WithName("GetCards")
            .WithTags("卡片綁定")
            .WithSummary("取得帳號目前綁定的卡片")
            .Produces<ResponseModel<CardInfoDto>>(StatusCodes.Status200OK);

        app.MapPost("/BindCard", async ([FromServices] ICardHttpServices svc, CardBindDto dto) =>
            Results.Ok(await svc.BindCardAsync(dto)))
            .WithName("BindCard")
            .WithTags("卡片綁定")
            .WithSummary("將 NFC 卡片綁定到帳號")
            .Produces<ResponseModel<CardInfoDto>>(StatusCodes.Status200OK);

        app.MapPost("/UnbindCard", async ([FromServices] ICardHttpServices svc, CardUnbindDto dto) =>
            Results.Ok(await svc.UnbindCardAsync(dto)))
            .WithName("UnbindCard")
            .WithTags("卡片綁定")
            .WithSummary("解除卡片綁定")
            .Produces<ResponseModel<string>>(StatusCodes.Status200OK);
    }
}
