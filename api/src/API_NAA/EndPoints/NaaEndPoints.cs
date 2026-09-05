using API_NAA.Dtos.Input.Create;
using API_NAA.Dtos.Input.Query;
using API_NAA.Dtos.Input.Update;
using API_NAA.Dtos.Output.Origin;
using API_NAA.Interfaces;
using Microsoft.AspNetCore.Mvc;
using ResponseModel;

namespace API_NAA.EndPoints;

public class NaaEndPoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/AuthenticateUser", async ([FromServices] IDbServices dbService, LoginRequestDto dto) =>
        {
            var result = await dbService.AuthenticateUserAsync(dto);
            return Results.Ok(result);
        })
        .WithName("AuthenticateUser")
        .WithTags("身分驗證")
        .WithSummary("從 SQL Server 驗證帳號與密碼")
        .Produces<ResponseModel<LoginResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/SaveHistory", async ([FromServices] IDbServices dbService, HistoryCreateDto dto) =>
        {
            var result = await dbService.SaveHistoryAsync(dto);
            return Results.Ok(result);
        })
        .WithName("SaveHistory")
        .WithTags("問答紀錄")
        .WithSummary("將問題、回答與對話資訊寫入 SQL Server")
        .Produces<ResponseModel<HistoryResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetConversationSummaries", async ([FromServices] IDbServices dbService, HistoryQueryDto dto) =>
        {
            var result = await dbService.GetConversationSummariesAsync(dto);
            return Results.Ok(result);
        })
        .WithName("GetConversationSummaries")
        .WithTags("問答紀錄")
        .WithSummary("取得指定帳號的對話摘要")
        .Produces<ResponseModel<ConversationSummaryDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetConversationById", async ([FromServices] IDbServices dbService, HistoryQueryDto dto) =>
        {
            var result = await dbService.GetConversationByIdAsync(dto);
            return Results.Ok(result);
        })
        .WithName("GetConversationById")
        .WithTags("問答紀錄")
        .WithSummary("依 conversationId 取得完整對話")
        .Produces<ResponseModel<HistoryResponseDto>>(StatusCodes.Status200OK);

        app.MapPost("/GetAgentContext", async ([FromServices] IDbServices dbService, HistoryQueryDto dto) =>
        {
            var result = await dbService.GetAgentContextAsync(dto);
            return Results.Ok(result);
        })
        .WithName("GetAgentContext")
        .WithTags("Agent 支援")
        .WithSummary("取得 AgentBuilder 延續對話所需內容")
        .Produces<ResponseModel<AgentContextDto>>(StatusCodes.Status200OK);

        app.MapPost("/SoftDeleteConversation", async ([FromServices] IDbServices dbService, ConversationMutationDto dto) =>
        {
            var result = await dbService.SoftDeleteConversationAsync(dto);
            return Results.Ok(result);
        })
        .WithName("SoftDeleteConversation")
        .WithTags("問答紀錄")
        .WithSummary("軟刪除指定對話")
        .Produces<ResponseModel<string>>(StatusCodes.Status200OK);

        app.MapPost("/RestoreConversation", async ([FromServices] IDbServices dbService, ConversationMutationDto dto) =>
        {
            var result = await dbService.RestoreConversationAsync(dto);
            return Results.Ok(result);
        })
        .WithName("RestoreConversation")
        .WithTags("問答紀錄")
        .WithSummary("還原已軟刪除的對話")
        .Produces<ResponseModel<string>>(StatusCodes.Status200OK);
    }
}
