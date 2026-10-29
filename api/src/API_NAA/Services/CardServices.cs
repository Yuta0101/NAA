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

public class CardServices : ICardServices
{
    private const string CardNotBound = "此卡片尚未綁定帳號";
    private const string InvalidCard = "無法辨識卡片內容，請重新感應";

    private readonly string _connectionString;
    private readonly ILogger<CardServices> _logger;

    public CardServices(IConfiguration configuration, ILogger<CardServices> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        _logger = logger;
    }

    public async Task<ResponseModel<LoginResponseDto>> AuthenticateByCardAsync(CardLoginRequestDto dto)
    {
        var cardUid = CardUid.Normalize(dto.CardUid);
        if (cardUid is null)
            return GenerateErrorResponse<LoginResponseDto>(InvalidCard);

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<LoginResponseDto>("Database connection is not configured");

        const string sql = """
            SELECT TOP (1) u.USER_ACCOUNT
            FROM dbo.USER_CARD AS c
            INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
            WHERE c.CARD_UID = @CardUid
              AND c.ENABLED = 1
              AND u.USER_ACCOUNT IS NOT NULL;
            """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@CardUid", SqlDbType.VarChar, CardUid.MaxLength).Value = cardUid;

            var value = await command.ExecuteScalarAsync();
            if (value is not string account)
            {
                _logger.LogInformation("Card login rejected for unbound card {Card}", CardUid.Mask(cardUid));
                return GenerateErrorResponse<LoginResponseDto>(CardNotBound);
            }

            return new[] { new LoginResponseDto { Account = account } }
                .ToResponse("登入成功", CardNotBound);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to authenticate card {Card}", CardUid.Mask(cardUid));
            return GenerateErrorResponse<LoginResponseDto>("登入服務暫時無法使用");
        }
    }

    public async Task<ResponseModel<CardInfoDto>> GetCardsAsync(CardQueryDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account))
            return GenerateErrorResponse<CardInfoDto>("account is required");

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<CardInfoDto>("Database connection is not configured");

        const string sql = """
            SELECT
                c.UNIQUE_ID,
                c.CARD_UID,
                COALESCE(c.UPDATE_DT, c.INSERT_DT) AS BOUND_AT
            FROM dbo.USER_CARD AS c
            INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
            WHERE u.USER_ACCOUNT = @Account
              AND c.ENABLED = 1
            ORDER BY BOUND_AT DESC, c.UNIQUE_ID;
            """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = dto.Account.Trim();

            var result = new List<CardInfoDto>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new CardInfoDto
                {
                    CardId = reader.GetString(0),
                    MaskedUid = CardUid.Mask(reader.GetString(1)),
                    BoundAt = ReadUtcDateTime(reader, 2)
                });
            }

            return result.ToResponse(DbConstant.QuerySuccess, DbConstant.QueryNoData);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to query cards for account {Account}", dto.Account);
            return GenerateErrorResponse<CardInfoDto>("Database query failed");
        }
    }

    public async Task<ResponseModel<CardInfoDto>> BindCardAsync(CardBindDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account))
            return GenerateErrorResponse<CardInfoDto>("account is required");

        var cardUid = CardUid.Normalize(dto.CardUid);
        if (cardUid is null)
            return GenerateErrorResponse<CardInfoDto>(InvalidCard);

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<CardInfoDto>("Database connection is not configured");

        var account = dto.Account.Trim();
        var now = DateTime.UtcNow;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

            try
            {
                var userId = await FindUserIdAsync(connection, transaction, account);
                if (userId is null)
                {
                    await transaction.RollbackAsync();
                    return GenerateErrorResponse<CardInfoDto>("找不到這個帳號");
                }

                var existing = await FindCardAsync(connection, transaction, cardUid);
                string cardId;
                string description;

                if (existing is null)
                {
                    cardId = Guid.NewGuid().ToString();
                    await InsertCardAsync(connection, transaction, cardId, userId, cardUid, account, now);
                    description = "卡片綁定成功";
                }
                else if (existing.Enabled && existing.UserId != userId)
                {
                    await transaction.RollbackAsync();
                    return GenerateErrorResponse<CardInfoDto>("這張卡片已綁定其他帳號");
                }
                else if (existing.Enabled)
                {
                    await transaction.RollbackAsync();
                    return new[]
                    {
                        new CardInfoDto
                        {
                            CardId = existing.CardId,
                            MaskedUid = CardUid.Mask(cardUid),
                            BoundAt = existing.BoundAt
                        }
                    }.ToResponse("這張卡片已綁定在你的帳號", CardNotBound);
                }
                else
                {
                    cardId = existing.CardId;
                    await ReactivateCardAsync(connection, transaction, cardId, userId, account, now);
                    description = "卡片綁定成功";
                }

                await transaction.CommitAsync();

                var card = new CardInfoDto
                {
                    CardId = cardId,
                    MaskedUid = CardUid.Mask(cardUid),
                    BoundAt = now
                };
                return new[] { card }.ToResponse(description, DbConstant.InsertError);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to bind card {Card} to account {Account}", CardUid.Mask(cardUid), account);
            return GenerateErrorResponse<CardInfoDto>("卡片綁定失敗");
        }
    }

    public async Task<ResponseModel<string>> UnbindCardAsync(CardUnbindDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Account) || string.IsNullOrWhiteSpace(dto.CardId))
            return GenerateErrorResponse<string>("account and cardId are required");

        if (!HasDatabaseConfiguration())
            return GenerateErrorResponse<string>("Database connection is not configured");

        const string sql = """
            UPDATE c
            SET c.ENABLED = 0,
                c.UPDATE_DT = @Now,
                c.UPDATE_OP = @UpdateOp
            FROM dbo.USER_CARD AS c
            INNER JOIN dbo.USER_ACCOUNT AS u ON u.UNIQUE_ID = c.USER_ID
            WHERE u.USER_ACCOUNT = @Account
              AND c.UNIQUE_ID = @CardId
              AND c.ENABLED = 1;
            """;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = DateTime.UtcNow;
            command.Parameters.Add("@UpdateOp", SqlDbType.VarChar, 50).Value = dto.Account.Trim();
            command.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = dto.Account.Trim();
            command.Parameters.Add("@CardId", SqlDbType.VarChar, 36).Value = dto.CardId.Trim();

            var affected = await command.ExecuteNonQueryAsync();
            return affected > 0
                ? new[] { dto.CardId.Trim() }.ToResponse("已解除卡片綁定", "找不到這張卡片")
                : Array.Empty<string>().ToResponse("已解除卡片綁定", "找不到這張卡片");
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to unbind card {CardId} for account {Account}", dto.CardId, dto.Account);
            return GenerateErrorResponse<string>("解除綁定失敗");
        }
    }

    private bool HasDatabaseConfiguration()
    {
        return !string.IsNullOrWhiteSpace(_connectionString) &&
               !_connectionString.Contains("YOUR_SERVER", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> FindUserIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string account)
    {
        const string sql = """
            SELECT TOP (1) UNIQUE_ID
            FROM dbo.USER_ACCOUNT
            WHERE USER_ACCOUNT = @Account;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@Account", SqlDbType.VarChar, 50).Value = account;
        return await command.ExecuteScalarAsync() as string;
    }

    private static async Task<CardState?> FindCardAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string cardUid)
    {
        // UPDLOCK + HOLDLOCK keeps two simultaneous bind requests for the same
        // card from both seeing "not bound" and racing on the unique index.
        const string sql = """
            SELECT TOP (1)
                UNIQUE_ID,
                USER_ID,
                ENABLED,
                COALESCE(UPDATE_DT, INSERT_DT) AS BOUND_AT
            FROM dbo.USER_CARD WITH (UPDLOCK, HOLDLOCK)
            WHERE CARD_UID = @CardUid;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@CardUid", SqlDbType.VarChar, CardUid.MaxLength).Value = cardUid;
        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new CardState(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                ReadUtcDateTime(reader, 3))
            : null;
    }

    private static async Task InsertCardAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string cardId,
        string userId,
        string cardUid,
        string actorAccount,
        DateTime now)
    {
        const string sql = """
            INSERT INTO dbo.USER_CARD
                (UNIQUE_ID, USER_ID, CARD_UID, ENABLED, INSERT_DT, INSERT_OP, UPDATE_DT, UPDATE_OP)
            VALUES
                (@UniqueId, @UserId, @CardUid, 1, @InsertDt, @InsertOp, NULL, NULL);
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@UniqueId", SqlDbType.VarChar, 36).Value = cardId;
        command.Parameters.Add("@UserId", SqlDbType.VarChar, 36).Value = userId;
        command.Parameters.Add("@CardUid", SqlDbType.VarChar, CardUid.MaxLength).Value = cardUid;
        command.Parameters.Add("@InsertDt", SqlDbType.DateTime2).Value = now;
        command.Parameters.Add("@InsertOp", SqlDbType.VarChar, 50).Value = actorAccount;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ReactivateCardAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string cardId,
        string userId,
        string actorAccount,
        DateTime now)
    {
        const string sql = """
            UPDATE dbo.USER_CARD
            SET USER_ID = @UserId,
                ENABLED = 1,
                UPDATE_DT = @UpdateDt,
                UPDATE_OP = @UpdateOp
            WHERE UNIQUE_ID = @UniqueId;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@UserId", SqlDbType.VarChar, 36).Value = userId;
        command.Parameters.Add("@UpdateDt", SqlDbType.DateTime2).Value = now;
        command.Parameters.Add("@UpdateOp", SqlDbType.VarChar, 50).Value = actorAccount;
        command.Parameters.Add("@UniqueId", SqlDbType.VarChar, 36).Value = cardId;
        await command.ExecuteNonQueryAsync();
    }

    private static DateTime? ReadUtcDateTime(SqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? null
            : DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc);
    }

    private sealed record CardState(string CardId, string UserId, bool Enabled, DateTime? BoundAt);
}
