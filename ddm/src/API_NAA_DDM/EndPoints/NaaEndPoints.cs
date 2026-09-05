using API_NAA_DDM.Dtos;
using API_NAA_DDM.Interfaces;
using API_NAA_DDM.Services;
using Microsoft.AspNetCore.Mvc;
using ResponseModel;

namespace API_NAA_DDM.EndPoints;

public class NaaEndPoints : INaaEndPoint
{
    public void NaaEndPoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/Login", async (INaaHttpServices svc, LoginRequestDto dto) =>
            Results.Ok(await svc.AuthenticateUserAsync(dto)))
            .WithName("Login")
            .WithTags("身分驗證")
            .WithSummary("驗證使用者帳號與密碼")
            .Produces<ResponseModel<LoginResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/SaveHistory", async (INaaHttpServices svc, HistoryCreateDto dto) =>
            Results.Ok(await svc.SaveHistoryAsync(dto)))
            .WithName("SaveHistory")
            .WithTags("問答紀錄")
            .WithSummary("儲存一筆問題與系統回答")
            .Produces<ResponseModel<HistoryResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetConversationSummaries", async (INaaHttpServices svc, HistoryQueryDto dto) =>
            Results.Ok(await svc.GetConversationSummariesAsync(dto)))
            .WithName("GetConversationSummaries")
            .WithTags("問答紀錄")
            .WithSummary("取得指定帳號的對話摘要")
            .Produces<ResponseModel<ConversationSummaryDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetConversationById", async (INaaHttpServices svc, HistoryQueryDto dto) =>
            Results.Ok(await svc.GetConversationByIdAsync(dto)))
            .WithName("GetConversationById")
            .WithTags("問答紀錄")
            .WithSummary("依 conversationId 取得完整對話")
            .Produces<ResponseModel<HistoryResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetUserByAccount", async (INaaHttpServices svc, UserQueryDto dto) =>
            Results.Ok(await svc.GetUserByAccountAsync(dto)))
            .WithName("GetUserByAccount")
            .WithTags("使用者")
            .WithSummary("取得本機模擬使用者資料（尚未串接資料庫）")
            .Produces<ResponseModel<UserQueryResponseDto>>(StatusCodes.Status200OK);

        app.MapPut("/UpdateUser", async (INaaHttpServices svc, UserQueryDto dto) =>
            Results.Ok(await svc.UpdateUserAsync(dto)))
            .WithName("UpdateUser")
            .WithTags("使用者")
            .WithSummary("模擬更新使用者（尚未寫入資料庫）")
            .Produces<ResponseModel<UserQueryResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/CreateUser", async (INaaHttpServices svc, UserQueryDto dto) =>
            Results.Ok(await svc.CreateUserAsync(dto)))
            .WithName("CreateUser")
            .WithTags("使用者")
            .WithSummary("模擬新增使用者（尚未寫入資料庫）")
            .Produces<ResponseModel<UserQueryResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/SoftDeleteConversation", async ([FromServices] INaaHttpServices svc, ConversationMutationDto dto) =>
            Results.Ok(await svc.SoftDeleteConversationAsync(dto)))
            .WithName("SoftDeleteConversation")
            .WithTags("問答紀錄")
            .WithSummary("軟刪除指定對話")
            .Produces<ResponseModel<string>>(StatusCodes.Status200OK);

        app.MapPost("/RestoreConversation", async ([FromServices] INaaHttpServices svc, ConversationMutationDto dto) =>
            Results.Ok(await svc.RestoreConversationAsync(dto)))
            .WithName("RestoreConversation")
            .WithTags("問答紀錄")
            .WithSummary("還原已軟刪除的對話")
            .Produces<ResponseModel<string>>(StatusCodes.Status200OK);

        app.MapPost("/ReviseText", async (ReviseRequestDto dto, INaaHttpServices svc) =>
        {
            try
            {
                var result = await svc.GenerateRevisedTextAsync(dto);
                return string.IsNullOrWhiteSpace(result)
                    ? Results.BadRequest("conversationId, account and inputText are required")
                    : Results.Ok(new ReviseResponseDto(result));
            }
            catch (AgentServiceException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        })
        .WithName("ReviseText")
        .WithTags("AI 問答")
        .WithSummary("將問題送至 AgentBuilder 並儲存問答紀錄")
        .WithDescription("AgentBuilder 尚未連通時，此端點會回傳 502。有效請求即使呼叫 Agent 失敗，也可能將失敗訊息寫入問答紀錄；測試前請使用專用測試帳號。")
        .Produces<ReviseResponseDto>(StatusCodes.Status200OK)
        .Produces<string>(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status502BadGateway);
    }
}
