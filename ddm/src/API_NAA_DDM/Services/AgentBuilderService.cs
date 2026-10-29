using API_NAA_DDM.Configs;
using API_NAA_DDM.Dtos;
using API_NAA_DDM.Interfaces;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace API_NAA_DDM.Services;

public sealed class AgentBuilderService : IAgentService
{
    private const string NoKnowledgeBaseDataMessage = "目前知識庫無資料";
    private static readonly string[] NoKnowledgeBaseDataSignals =
    [
        NoKnowledgeBaseDataMessage,
        "未檢索到與此問題直接相關",
        "未檢索到相關內容",
        "目前知識庫未提供",
        "知識庫中沒有相關",
        "知識庫沒有相關",
        "知識庫未找到",
        "知識庫中未找到",
        "知識庫中查無",
        "無法在知識庫中找到",
        "無法根據目前知識庫回答",
        "無法回答此問題",
        "沒有足夠的資料回答",
        "找不到相關資料",
        "查無相關資料",
        "知識庫無相關資料"
    ];

    private readonly HttpClient _client;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentBuilderService> _logger;

    public AgentBuilderService(
        HttpClient client,
        IOptions<AgentOptions> options,
        ILogger<AgentBuilderService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AgentMessageResponse> GenerateResponseAsync(
        AgentMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InputText))
            throw new AgentServiceException("請輸入問題。");

        var result = await SendMessageAsync(
            request.InputText.Trim(),
            request.AgentThreadId ?? string.Empty,
            request.UserId,
            cancellationToken);

        var answer = result.Answer?.Trim();
        if (string.IsNullOrWhiteSpace(answer))
            answer = NoKnowledgeBaseDataMessage;

        if (IsNoKnowledgeBaseDataAnswer(answer))
            answer = NoKnowledgeBaseDataMessage;

        return new AgentMessageResponse(answer, result.ConversationId);
    }

    private async Task<AgentBuilderChatResponse> SendMessageAsync(
        string query,
        string conversationId,
        string userId,
        CancellationToken cancellationToken)
    {
        var payload = new AgentBuilderChatRequest
        {
            Query = query,
            ConversationId = conversationId,
            User = userId,
            ResponseMode = "blocking"
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, _options.ChatPath)
        {
            Content = JsonContent.Create(payload)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            using var response = await _client.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "AgentBuilder request failed with HTTP {StatusCode}",
                    (int)response.StatusCode);
                throw new AgentServiceException($"醫院 Agent 回應 HTTP {(int)response.StatusCode}。");
            }

            return await response.Content.ReadFromJsonAsync<AgentBuilderChatResponse>(
                       cancellationToken: cancellationToken)
                   ?? throw new AgentServiceException("醫院 Agent 回傳空白資料。");
        }
        catch (AgentServiceException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AgentServiceException("醫院 Agent 連線逾時。");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "AgentBuilder connection failed");
            throw new AgentServiceException("無法連線到醫院 Agent，請確認院內網路或 VPN 與 DNS。");
        }
    }

    private static bool IsNoKnowledgeBaseDataAnswer(string answer)
    {
        return answer.Length <= 500
            && NoKnowledgeBaseDataSignals.Any(signal =>
                answer.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class AgentBuilderChatRequest
    {
        [JsonPropertyName("inputs")]
        public Dictionary<string, object?> Inputs { get; init; } = new();

        [JsonPropertyName("query")]
        public string Query { get; init; } = string.Empty;

        [JsonPropertyName("response_mode")]
        public string ResponseMode { get; init; } = "blocking";

        [JsonPropertyName("conversation_id")]
        public string ConversationId { get; init; } = string.Empty;

        [JsonPropertyName("user")]
        public string User { get; init; } = string.Empty;
    }

    private sealed class AgentBuilderChatResponse
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; init; }

        [JsonPropertyName("conversation_id")]
        public string? ConversationId { get; init; }
    }
}

public sealed class AgentServiceException(string message) : Exception(message);
