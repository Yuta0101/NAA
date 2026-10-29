using API_NAA_DDM.Constants;
using API_NAA_DDM.Dtos;
using API_NAA_DDM.Interfaces;
using ResponseModel;
using System.Text.Json;
using static ResponseModel.ResponseMapper;

namespace API_NAA_DDM.Services;

public class NaaHttpServices : INaaHttpServices
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client;
    private readonly IAgentService _agentService;
    private readonly ILogger<NaaHttpServices> _logger;

    public NaaHttpServices(
        HttpClient client,
        IAgentService agentService,
        ILogger<NaaHttpServices> logger)
    {
        _client = client;
        _agentService = agentService;
        _logger = logger;
    }

    public Task<ResponseModel<LoginResponseDto>> AuthenticateUserAsync(LoginRequestDto dto)
    {
        return PostToNaaAsync<LoginRequestDto, LoginResponseDto>("/AuthenticateUser", dto);
    }

    public Task<ResponseModel<HistoryResponseDto>> SaveHistoryAsync(HistoryCreateDto dto)
    {
        return PostToNaaAsync<HistoryCreateDto, HistoryResponseDto>("/SaveHistory", dto);
    }

    public Task<ResponseModel<ConversationSummaryDto>> GetConversationSummariesAsync(HistoryQueryDto dto)
    {
        return PostToNaaAsync<HistoryQueryDto, ConversationSummaryDto>("/GetConversationSummaries", dto);
    }

    public Task<ResponseModel<HistoryResponseDto>> GetConversationByIdAsync(HistoryQueryDto dto)
    {
        return PostToNaaAsync<HistoryQueryDto, HistoryResponseDto>("/GetConversationById", dto);
    }

    public Task<ResponseModel<AgentContextDto>> GetAgentContextAsync(HistoryQueryDto dto)
    {
        return PostToNaaAsync<HistoryQueryDto, AgentContextDto>("/GetAgentContext", dto);
    }

    public Task<ResponseModel<string>> SoftDeleteConversationAsync(ConversationMutationDto dto)
    {
        return PostToNaaAsync<ConversationMutationDto, string>("/SoftDeleteConversation", dto);
    }

    public Task<ResponseModel<string>> RestoreConversationAsync(ConversationMutationDto dto)
    {
        return PostToNaaAsync<ConversationMutationDto, string>("/RestoreConversation", dto);
    }

    public Task<ResponseModel<UserQueryResponseDto>> GetUserByAccountAsync(UserQueryDto dto)
    {
        return Task.FromResult(LocalUserResponse(dto.Account, "Local user loaded"));
    }

    public Task<ResponseModel<UserQueryResponseDto>> UpdateUserAsync(UserQueryDto dto)
    {
        return Task.FromResult(LocalUserResponse(dto.Account, "Local user updated"));
    }

    public Task<ResponseModel<UserQueryResponseDto>> CreateUserAsync(UserQueryDto dto)
    {
        return Task.FromResult(LocalUserResponse(dto.Account, "Local user created"));
    }

    public async Task<string?> GenerateRevisedTextAsync(ReviseRequestDto reviseRequestDto)
    {
        if (string.IsNullOrWhiteSpace(reviseRequestDto.Account) ||
            string.IsNullOrWhiteSpace(reviseRequestDto.InputText) ||
            string.IsNullOrWhiteSpace(reviseRequestDto.ConversationId))
        {
            return null;
        }

        var contextResult = await GetAgentContextAsync(new HistoryQueryDto
        {
            Account = reviseRequestDto.Account,
            ConversationId = reviseRequestDto.ConversationId,
            OriginCode = reviseRequestDto.OriginCode
        });

        if (contextResult.Status != 1 && contextResult.Description != DbConstant.QueryNoData)
            throw new AgentServiceException("無法取得 Agent 對話狀態，已停止送出以避免建立錯誤的新對話。");

        var existingAgentThreadId = contextResult.Results.FirstOrDefault()?.AgentThreadId;
        AgentMessageResponse agentResult;

        try
        {
            agentResult = await _agentService.GenerateResponseAsync(new AgentMessageRequest(
                reviseRequestDto.InputText,
                reviseRequestDto.Account,
                existingAgentThreadId));
        }
        catch (AgentServiceException ex)
        {
            var failureAnswer = $"系統暫時無法取得回答：{ex.Message}";
            var failureSaveResult = await SaveHistoryAsync(CreateHistoryDto(
                reviseRequestDto,
                failureAnswer,
                existingAgentThreadId));

            if (failureSaveResult.Status != 1)
            {
                _logger.LogWarning(
                    "Agent request and conversation history save both failed: {Description}",
                    failureSaveResult.Description);
                throw new AgentServiceException(
                    $"{ex.Message}；這次問題的歷史紀錄也未能儲存：{failureSaveResult.Description}");
            }

            throw;
        }

        var revisedText = agentResult.Answer;

        if (string.IsNullOrWhiteSpace(revisedText))
        {
            const string failureAnswer = "系統暫時沒有回傳回答內容。";
            var failureSaveResult = await SaveHistoryAsync(CreateHistoryDto(
                reviseRequestDto,
                failureAnswer,
                agentResult.AgentThreadId ?? existingAgentThreadId));

            if (failureSaveResult.Status != 1)
            {
                throw new AgentServiceException(
                    $"Agent 沒有回傳回答，且歷史紀錄儲存失敗：{failureSaveResult.Description}");
            }

            throw new AgentServiceException(failureAnswer);
        }

        var saveResult = await SaveHistoryAsync(CreateHistoryDto(
            reviseRequestDto,
            revisedText,
            agentResult.AgentThreadId ?? existingAgentThreadId));
        if (saveResult.Status != 1)
        {
            _logger.LogWarning(
                "Agent response was generated, but conversation history was not saved: {Description}",
                saveResult.Description);
            throw new AgentServiceException(
                $"Agent 已回覆，但歷史紀錄儲存失敗：{saveResult.Description}");
        }

        return revisedText;
    }

    private static HistoryCreateDto CreateHistoryDto(
        ReviseRequestDto request,
        string answerText,
        string? agentThreadId)
    {
        return new HistoryCreateDto
        {
            ConversationId = request.ConversationId,
            AgentThreadId = agentThreadId,
            Account = request.Account!,
            QuestionText = request.InputText!,
            AnswerText = answerText,
            // Each conversation row represents one turn. Store that turn's
            // question as its title instead of repeating the first turn title.
            ChatTitle = request.InputText,
            OriginCode = request.OriginCode
        };
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

            var result = await response.Content.ReadFromJsonAsync<ResponseModel<TResponse>>(
                JsonOptions);

            return result ?? GenerateErrorResponse<TResponse>("NAA API returned an empty response");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NAA API call failed: {Path}", path);
            return GenerateErrorResponse<TResponse>("NAA API request failed");
        }
    }

    private static ResponseModel<UserQueryResponseDto> LocalUserResponse(string? account, string description)
    {
        var user = new UserQueryResponseDto
        {
            UniqueId = string.IsNullOrWhiteSpace(account) ? Guid.NewGuid().ToString() : account,
            UserAccount1 = string.IsNullOrWhiteSpace(account) ? "local.user" : account,
            InsertDt = DateTime.UtcNow,
            UpdateDt = null
        };

        return new[] { user }.ToResponse(description, "No local user data");
    }
}
