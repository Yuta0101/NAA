using API_NAA.Constants;
using API_NAA.Dtos.Input.Create;
using API_NAA.Dtos.Input.Query;
using API_NAA.Dtos.Input.Update;
using API_NAA.Dtos.Output.Origin;
using API_NAA.Interfaces;
using Microsoft.Data.SqlClient;
using ResponseModel;
using System.Data;
using static ResponseModel.ResponseMapper;

namespace API_NAA.Services;

public class DbServices : IDbServices
{
    private readonly string _connectionString;
    private readonly ILogger<DbServices> _logger;

    public DbServices(IConfiguration configuration, ILogger<DbServices> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        _logger = logger;
    }

    public async Task<ResponseModel<LoginResponseDto>> AuthenticateUserAsync(LoginRequestDto dto)
    {
        const string invalidCredentials = "帳號或密碼錯誤";

        if (string.IsNullOrWhiteSpace(dto.Account) || string.IsNullOrEmpty(dto.Password))
            return GenerateErrorResponse<LoginResponseDto>(invalidCredentials);

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<LoginResponseDto>("Database connection is not configured");

        var account = dto.Account.Trim();
        if (account.Length > 50 || dto.Password.Length > 256)
            return GenerateErrorResponse<LoginResponseDto>(invalidCredentials);

        const string sql = """
            SELECT TOP (1)
                USER_ACCOUNT,
                PASSWORD_HASH
            FROM dbo.USER_ACCOUNT
            WHERE USER_ACCOUNT = @Account;
            """;

        string authenticatedAccount;
        string passwordHash;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = account;
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow);

            if (!await reader.ReadAsync() || reader.IsDBNull(1))
            {
                return GenerateErrorResponse<LoginResponseDto>(invalidCredentials);
            }

            authenticatedAccount = reader.GetString(0);
            passwordHash = reader.GetString(1);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to authenticate account {Account}", account);
            return GenerateErrorResponse<LoginResponseDto>("登入服務暫時無法使用");
        }

        // PBKDF2 is deliberately CPU intensive. Verify after releasing the SQL
        // reader and connection so concurrent logins do not exhaust the pool.
        if (!PasswordHashUtility.Verify(dto.Password, passwordHash))
            return GenerateErrorResponse<LoginResponseDto>(invalidCredentials);

        var result = new LoginResponseDto { Account = authenticatedAccount };
        return new[] { result }.ToResponse("登入成功", invalidCredentials);
    }

    public async Task<ResponseModel<HistoryResponseDto>> SaveHistoryAsync(HistoryCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account) ||
            string.IsNullOrWhiteSpace(dto.QuestionText) ||
            string.IsNullOrWhiteSpace(dto.AnswerText) ||
            string.IsNullOrWhiteSpace(dto.OriginCode))
        {
            return GenerateErrorResponse<HistoryResponseDto>(
                "account, questionText, answerText, originCode are required");
        }

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<HistoryResponseDto>("Database connection is not configured");

        var account = dto.Account.Trim();
        var originCode = dto.OriginCode.Trim();
        var conversationId = string.IsNullOrWhiteSpace(dto.ConversationId)
            ? Guid.NewGuid().ToString()
            : dto.ConversationId.Trim();

        if (account.Length > 50)
            return GenerateErrorResponse<HistoryResponseDto>("account cannot exceed 50 characters");

        if (originCode.Length > 50)
            return GenerateErrorResponse<HistoryResponseDto>("originCode cannot exceed 50 characters");

        if (conversationId.Length > 36)
            return GenerateErrorResponse<HistoryResponseDto>("conversationId cannot exceed 36 characters");

        if (dto.AgentThreadId?.Length > 128)
            return GenerateErrorResponse<HistoryResponseDto>("agentThreadId cannot exceed 128 characters");

        var questionId = Guid.NewGuid().ToString();
        var answerId = Guid.NewGuid().ToString();
        var conversationRecordId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                var existingConversation = await GetConversationStateAsync(
                    connection,
                    transaction,
                    conversationId);

                if (existingConversation is not null &&
                    !string.Equals(existingConversation.Account, account, StringComparison.OrdinalIgnoreCase))
                {
                    await transaction.RollbackAsync();
                    return GenerateErrorResponse<HistoryResponseDto>("Conversation is not available for this account");
                }

                if (existingConversation?.IsDeleted == true)
                {
                    await transaction.RollbackAsync();
                    return GenerateErrorResponse<HistoryResponseDto>("Deleted conversation must be restored before adding messages");
                }

                var userId = await GetOrCreateUserAsync(connection, transaction, account, account, now);

                await InsertQuestionAsync(connection, transaction, questionId, dto.QuestionText, originCode, account, now);
                await InsertAnswerAsync(connection, transaction, answerId, questionId, dto.AnswerText, originCode, account, now);
                await LinkQuestionToAnswerAsync(connection, transaction, questionId, answerId, account, now);
                await InsertConversationAsync(
                    connection,
                    transaction,
                    conversationRecordId,
                    conversationId,
                    userId,
                    questionId,
                    answerId,
                    dto.AgentThreadId,
                    dto.ChatTitle,
                    originCode,
                    account,
                    now);

                await transaction.CommitAsync();

                var history = new HistoryResponseDto
                {
                    UniqueId = conversationRecordId,
                    ConversationId = conversationId,
                    Account = account,
                    ChatTitle = dto.ChatTitle,
                    OriginCode = originCode,
                    QuestionText = dto.QuestionText,
                    AnswerText = dto.AnswerText,
                    InsertDt = now
                };

                return new[] { history }.ToResponse(DbConstant.InsertSuccess, DbConstant.InsertError);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to save history for account {Account}", account);
            return GenerateErrorResponse<HistoryResponseDto>("Database save failed");
        }
    }

    public async Task<ResponseModel<ConversationSummaryDto>> GetConversationSummariesAsync(HistoryQueryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account))
            return GenerateErrorResponse<ConversationSummaryDto>("account is required");

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<ConversationSummaryDto>("Database connection is not configured");

        const string sql = """
            WITH RankedConversations AS
            (
                SELECT
                    c.CONVERSATION_ID,
                    u.USER_ACCOUNT,
                    c.CHAT_TITLE,
                    q.QUESTION_TEXT,
                    a.ANSWER_TEXT,
                    c.ORIGIN_CODE,
                    MAX(c.INSERT_DT) OVER
                    (
                        PARTITION BY c.CONVERSATION_ID
                    ) AS LAST_MESSAGE_AT,
                    COUNT(*) OVER (PARTITION BY c.CONVERSATION_ID) AS TURN_COUNT,
                    c.IS_DELETED,
                    c.DELETED_AT,
                    c.DELETED_BY,
                    ROW_NUMBER() OVER
                    (
                        PARTITION BY c.CONVERSATION_ID
                        ORDER BY c.INSERT_DT ASC, c.UNIQUE_ID ASC
                    ) AS ROW_NUMBER
                FROM dbo.CONVERSATION_RECORDS AS c
                INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
                INNER JOIN dbo.QUESTIONS_RECORDS AS q ON q.UNIQUE_ID = c.QUESTION_ID
                INNER JOIN dbo.ANSWERS_RECORDS AS a ON a.UNIQUE_ID = c.ANSWER_ID
                WHERE u.USER_ACCOUNT = @Account
                  AND c.IS_DELETED = @IsDeleted
                  AND (@OriginCode IS NULL OR c.ORIGIN_CODE = @OriginCode)
            )
            SELECT
                CONVERSATION_ID,
                USER_ACCOUNT,
                CHAT_TITLE,
                QUESTION_TEXT,
                ANSWER_TEXT,
                ORIGIN_CODE,
                LAST_MESSAGE_AT,
                TURN_COUNT,
                IS_DELETED,
                DELETED_AT,
                DELETED_BY
            FROM RankedConversations
            WHERE ROW_NUMBER = 1
            ORDER BY LAST_MESSAGE_AT DESC;
            """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            AddHistoryQueryParameters(command, dto);
            command.Parameters.Add("@IsDeleted", SqlDbType.Bit).Value = dto.IsDeleted;

            var result = new List<ConversationSummaryDto>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new ConversationSummaryDto
                {
                    ConversationId = reader.GetString(0),
                    Account = reader.IsDBNull(1) ? null : reader.GetString(1),
                    ChatTitle = reader.IsDBNull(2) ? null : reader.GetString(2),
                    LastQuestionText = reader.IsDBNull(3) ? null : reader.GetString(3),
                    LastAnswerText = reader.IsDBNull(4) ? null : reader.GetString(4),
                    OriginCode = reader.IsDBNull(5) ? null : reader.GetString(5),
                    LastMessageAt = ReadUtcDateTime(reader, 6),
                    TurnCount = reader.GetInt32(7),
                    IsDeleted = reader.GetBoolean(8),
                    DeletedAt = ReadUtcDateTime(reader, 9),
                    DeletedBy = reader.IsDBNull(10) ? null : reader.GetString(10)
                });
            }

            return result.ToResponse(DbConstant.QuerySuccess, DbConstant.QueryNoData);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to query conversation summaries for account {Account}", dto.Account);
            return GenerateErrorResponse<ConversationSummaryDto>("Database query failed");
        }
    }

    public async Task<ResponseModel<HistoryResponseDto>> GetConversationByIdAsync(HistoryQueryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account) || string.IsNullOrWhiteSpace(dto.ConversationId))
            return GenerateErrorResponse<HistoryResponseDto>("account and conversationId are required");

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<HistoryResponseDto>("Database connection is not configured");

        const string sql = """
            SELECT
                c.UNIQUE_ID,
                c.CONVERSATION_ID,
                u.USER_ACCOUNT,
                c.CHAT_TITLE,
                q.QUESTION_TEXT,
                a.ANSWER_TEXT,
                c.ORIGIN_CODE,
                c.INSERT_DT,
                c.IS_DELETED,
                c.DELETED_AT,
                c.DELETED_BY
            FROM dbo.CONVERSATION_RECORDS AS c
            INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
            INNER JOIN dbo.QUESTIONS_RECORDS AS q ON q.UNIQUE_ID = c.QUESTION_ID
            INNER JOIN dbo.ANSWERS_RECORDS AS a ON a.UNIQUE_ID = c.ANSWER_ID
            WHERE u.USER_ACCOUNT = @Account
              AND c.CONVERSATION_ID = @ConversationId
              AND (@OriginCode IS NULL OR c.ORIGIN_CODE = @OriginCode)
              AND (@IncludeDeleted = 1 OR c.IS_DELETED = 0)
            ORDER BY c.INSERT_DT, c.UNIQUE_ID;
            """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            AddHistoryQueryParameters(command, dto);
            command.Parameters.Add("@ConversationId", SqlDbType.VarChar, 36).Value = dto.ConversationId.Trim();
            command.Parameters.Add("@IncludeDeleted", SqlDbType.Bit).Value = dto.IncludeDeleted;

            var result = new List<HistoryResponseDto>();
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new HistoryResponseDto
                {
                    UniqueId = reader.GetString(0),
                    ConversationId = reader.GetString(1),
                    Account = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ChatTitle = reader.IsDBNull(3) ? null : reader.GetString(3),
                    QuestionText = reader.IsDBNull(4) ? null : reader.GetString(4),
                    AnswerText = reader.IsDBNull(5) ? null : reader.GetString(5),
                    OriginCode = reader.IsDBNull(6) ? null : reader.GetString(6),
                    InsertDt = ReadUtcDateTime(reader, 7),
                    IsDeleted = reader.GetBoolean(8),
                    DeletedAt = ReadUtcDateTime(reader, 9),
                    DeletedBy = reader.IsDBNull(10) ? null : reader.GetString(10)
                });
            }

            return result.ToResponse(DbConstant.QuerySuccess, DbConstant.QueryNoData);
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Failed to query conversation {ConversationId} for account {Account}",
                dto.ConversationId,
                dto.Account);
            return GenerateErrorResponse<HistoryResponseDto>("Database query failed");
        }
    }

    public Task<ResponseModel<string>> SoftDeleteConversationAsync(ConversationMutationDto dto)
    {
        return SetConversationDeletedStateAsync(dto, isDeleted: true);
    }

    public async Task<ResponseModel<AgentContextDto>> GetAgentContextAsync(HistoryQueryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account) || string.IsNullOrWhiteSpace(dto.ConversationId))
            return GenerateErrorResponse<AgentContextDto>("account and conversationId are required");

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<AgentContextDto>("Database connection is not configured");

        const string sql = """
            SELECT TOP (1) c.AGENT_THREAD_ID
            FROM dbo.CONVERSATION_RECORDS AS c
            INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
            WHERE u.USER_ACCOUNT = @Account
              AND c.CONVERSATION_ID = @ConversationId
              AND c.IS_DELETED = 0
              AND (@OriginCode IS NULL OR c.ORIGIN_CODE = @OriginCode)
              AND c.AGENT_THREAD_ID IS NOT NULL
            ORDER BY c.INSERT_DT DESC, c.UNIQUE_ID DESC;
            """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            AddHistoryQueryParameters(command, dto);
            command.Parameters.Add("@ConversationId", SqlDbType.VarChar, 36).Value = dto.ConversationId.Trim();

            var value = await command.ExecuteScalarAsync();
            var result = value is string agentThreadId
                ? new[] { new AgentContextDto { AgentThreadId = agentThreadId } }
                : Array.Empty<AgentContextDto>();

            return result.ToResponse(DbConstant.QuerySuccess, DbConstant.QueryNoData);
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Failed to query Agent context for conversation {ConversationId} and account {Account}",
                dto.ConversationId,
                dto.Account);
            return GenerateErrorResponse<AgentContextDto>("Database query failed");
        }
    }

    public Task<ResponseModel<string>> RestoreConversationAsync(ConversationMutationDto dto)
    {
        return SetConversationDeletedStateAsync(dto, isDeleted: false);
    }

    private async Task<ResponseModel<string>> SetConversationDeletedStateAsync(
        ConversationMutationDto dto,
        bool isDeleted)
    {
        if (string.IsNullOrWhiteSpace(dto.Account) || string.IsNullOrWhiteSpace(dto.ConversationId))
            return GenerateErrorResponse<string>("account and conversationId are required");

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<string>("Database connection is not configured");

        const string sql = """
            UPDATE c
            SET c.IS_DELETED = @IsDeleted,
                c.DELETED_AT = CASE WHEN @IsDeleted = 1 THEN @Now ELSE NULL END,
                c.DELETED_BY = CASE WHEN @IsDeleted = 1 THEN @DeletedBy ELSE NULL END,
                c.UPDATE_DT = @Now,
                c.UPDATE_OP = @UpdateOp
            FROM dbo.CONVERSATION_RECORDS AS c
            INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
            WHERE u.USER_ACCOUNT = @Account
              AND c.CONVERSATION_ID = @ConversationId
              AND c.IS_DELETED <> @IsDeleted;
            """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@IsDeleted", SqlDbType.Bit).Value = isDeleted;
            command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = DateTime.UtcNow;
            command.Parameters.Add("@DeletedBy", SqlDbType.VarChar, 50).Value = dto.Account.Trim();
            command.Parameters.Add("@UpdateOp", SqlDbType.VarChar, 50).Value = dto.Account.Trim();
            command.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = dto.Account.Trim();
            command.Parameters.Add("@ConversationId", SqlDbType.VarChar, 36).Value = dto.ConversationId.Trim();

            var affected = await command.ExecuteNonQueryAsync();
            var success = isDeleted ? "Conversation moved to deleted items" : "Conversation restored";
            var noData = isDeleted ? "Conversation not found or already deleted" : "Conversation not found or already active";

            return affected > 0
                ? new[] { dto.ConversationId.Trim() }.ToResponse(success, noData)
                : Array.Empty<string>().ToResponse(success, noData);
        }
        catch (SqlException ex)
        {
            _logger.LogError(
                ex,
                "Failed to change deleted state for conversation {ConversationId} and account {Account}",
                dto.ConversationId,
                dto.Account);
            return GenerateErrorResponse<string>("Database update failed");
        }
    }

    private bool HasDatabaseConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_connectionString) &&
               !_connectionString.Contains("YOUR_SERVER", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddHistoryQueryParameters(SqlCommand command, HistoryQueryDto dto)
    {
        command.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = dto.Account!.Trim();
        command.Parameters.Add("@OriginCode", SqlDbType.VarChar, 50).Value =
            string.IsNullOrWhiteSpace(dto.OriginCode) ? DBNull.Value : dto.OriginCode.Trim();
    }

    private static async Task<ConversationState?> GetConversationStateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string conversationId)
    {
        const string sql = """
            SELECT TOP (1)
                u.USER_ACCOUNT,
                c.IS_DELETED
            FROM dbo.CONVERSATION_RECORDS AS c WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
            WHERE c.CONVERSATION_ID = @ConversationId;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@ConversationId", SqlDbType.VarChar, 36).Value = conversationId;
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new ConversationState(reader.GetString(0), reader.GetBoolean(1))
            : null;
    }

    private static async Task<string> GetOrCreateUserAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string account,
        string actorAccount,
        DateTime now)
    {
        const string findSql = """
            SELECT TOP (1) UNIQUE_ID
            FROM dbo.USER_ACCOUNT WITH (UPDLOCK, HOLDLOCK)
            WHERE USER_ACCOUNT = @Account
            ORDER BY INSERT_DT;
            """;

        await using var findCommand = new SqlCommand(findSql, connection, transaction);
        findCommand.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = account;
        var existingId = await findCommand.ExecuteScalarAsync();

        if (existingId is string userId)
            return userId;

        var newUserId = Guid.NewGuid().ToString();
        const string insertSql = """
            INSERT INTO dbo.USER_ACCOUNT
                (UNIQUE_ID, INSERT_DT, INSERT_OP, UPDATE_DT, UPDATE_OP, USER_ACCOUNT)
            VALUES
                (@UniqueId, @InsertDt, @InsertOp, NULL, NULL, @Account);
            """;

        await using var insertCommand = new SqlCommand(insertSql, connection, transaction);
        insertCommand.Parameters.Add("@UniqueId", SqlDbType.VarChar, 36).Value = newUserId;
        insertCommand.Parameters.Add("@InsertDt", SqlDbType.DateTime2).Value = now;
        insertCommand.Parameters.Add("@InsertOp", SqlDbType.VarChar, 50).Value = actorAccount;
        insertCommand.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = account;
        await insertCommand.ExecuteNonQueryAsync();
        return newUserId;
    }

    private static async Task InsertQuestionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string questionId,
        string questionText,
        string originCode,
        string actorAccount,
        DateTime now)
    {
        const string sql = """
            INSERT INTO dbo.QUESTIONS_RECORDS
                (UNIQUE_ID, QUESTION_TEXT, INSERT_OP, INSERT_DT, UPDATE_DT, UPDATE_OP, ORIGIN_CODE)
            VALUES
                (@UniqueId, @QuestionText, @InsertOp, @InsertDt, NULL, NULL, @OriginCode);
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@UniqueId", SqlDbType.VarChar, 36).Value = questionId;
        command.Parameters.Add("@QuestionText", SqlDbType.NVarChar, -1).Value = questionText;
        command.Parameters.Add("@InsertOp", SqlDbType.VarChar, 50).Value = actorAccount;
        command.Parameters.Add("@InsertDt", SqlDbType.DateTime2).Value = now;
        command.Parameters.Add("@OriginCode", SqlDbType.VarChar, 50).Value = originCode;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertAnswerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string answerId,
        string questionId,
        string answerText,
        string originCode,
        string actorAccount,
        DateTime now)
    {
        const string sql = """
            INSERT INTO dbo.ANSWERS_RECORDS
                (UNIQUE_ID, QUESTION_ID, ANSWER_TEXT, INSERT_OP, INSERT_DT, UPDATE_DT, UPDATE_OP, ORIGIN_CODE)
            VALUES
                (@UniqueId, @QuestionId, @AnswerText, @InsertOp, @InsertDt, NULL, NULL, @OriginCode);
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@UniqueId", SqlDbType.VarChar, 36).Value = answerId;
        command.Parameters.Add("@QuestionId", SqlDbType.VarChar, 36).Value = questionId;
        command.Parameters.Add("@AnswerText", SqlDbType.NVarChar, -1).Value = answerText;
        command.Parameters.Add("@InsertOp", SqlDbType.VarChar, 50).Value = actorAccount;
        command.Parameters.Add("@InsertDt", SqlDbType.DateTime2).Value = now;
        command.Parameters.Add("@OriginCode", SqlDbType.VarChar, 50).Value = originCode;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task LinkQuestionToAnswerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string questionId,
        string answerId,
        string actorAccount,
        DateTime now)
    {
        const string sql = """
            UPDATE dbo.QUESTIONS_RECORDS
            SET ANSWER_ID = @AnswerId,
                UPDATE_OP = @UpdateOp,
                UPDATE_DT = @UpdateDt
            WHERE UNIQUE_ID = @QuestionId;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@AnswerId", SqlDbType.VarChar, 36).Value = answerId;
        command.Parameters.Add("@UpdateOp", SqlDbType.VarChar, 50).Value = actorAccount;
        command.Parameters.Add("@UpdateDt", SqlDbType.DateTime2).Value = now;
        command.Parameters.Add("@QuestionId", SqlDbType.VarChar, 36).Value = questionId;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertConversationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string conversationRecordId,
        string conversationId,
        string userId,
        string questionId,
        string answerId,
        string? agentThreadId,
        string? chatTitle,
        string originCode,
        string actorAccount,
        DateTime now)
    {
        const string sql = """
            INSERT INTO dbo.CONVERSATION_RECORDS
                (UNIQUE_ID, CONVERSATION_ID, AGENT_THREAD_ID, USER_ID, QUESTION_ID, ANSWER_ID, CHAT_TITLE, INSERT_OP, INSERT_DT, UPDATE_DT, UPDATE_OP, ORIGIN_CODE, IS_DELETED)
            VALUES
                (@UniqueId, @ConversationId, @AgentThreadId, @UserId, @QuestionId, @AnswerId, @ChatTitle, @InsertOp, @InsertDt, NULL, NULL, @OriginCode, 0);
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@UniqueId", SqlDbType.VarChar, 36).Value = conversationRecordId;
        command.Parameters.Add("@ConversationId", SqlDbType.VarChar, 36).Value = conversationId;
        command.Parameters.Add("@AgentThreadId", SqlDbType.NVarChar, 128).Value =
            string.IsNullOrWhiteSpace(agentThreadId) ? DBNull.Value : agentThreadId;
        command.Parameters.Add("@UserId", SqlDbType.VarChar, 36).Value = userId;
        command.Parameters.Add("@QuestionId", SqlDbType.VarChar, 36).Value = questionId;
        command.Parameters.Add("@AnswerId", SqlDbType.VarChar, 36).Value = answerId;
        command.Parameters.Add("@ChatTitle", SqlDbType.NVarChar, 255).Value =
            string.IsNullOrWhiteSpace(chatTitle) ? DBNull.Value : chatTitle[..Math.Min(chatTitle.Length, 255)];
        command.Parameters.Add("@InsertOp", SqlDbType.VarChar, 50).Value = actorAccount;
        command.Parameters.Add("@InsertDt", SqlDbType.DateTime2).Value = now;
        command.Parameters.Add("@OriginCode", SqlDbType.VarChar, 50).Value = originCode;
        await command.ExecuteNonQueryAsync();
    }

    private static DateTime? ReadUtcDateTime(SqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    private sealed record ConversationState(string Account, bool IsDeleted);
}
