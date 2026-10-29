using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var webJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var ddmBaseUrl = builder.Configuration["Ddm:BaseUrl"]
    ?? Environment.GetEnvironmentVariable("DDM_BASE_URL")
    ?? "https://localhost:7079";
var ddmTimeoutSeconds = builder.Configuration.GetValue("Ddm:TimeoutSeconds", 150);

if (!Uri.TryCreate(ddmBaseUrl, UriKind.Absolute, out var ddmBaseUri))
    throw new InvalidOperationException($"Ddm:BaseUrl 不是有效網址：{ddmBaseUrl}");

if (ddmBaseUri.Scheme != Uri.UriSchemeHttps)
    throw new InvalidOperationException("DDM must use HTTPS.");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddHttpClient("DDM", client =>
{
    client.BaseAddress = ddmBaseUri;
    client.Timeout = TimeSpan.FromSeconds(Math.Clamp(ddmTimeoutSeconds, 30, 300));
});
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "NAA.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
// A card UID is not a secret, so both login endpoints are limited per client
// address to slow down anyone trying to guess bound UIDs.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "嘗試次數過多，請稍後再試。" },
            cancellationToken);
    };
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseResponseCompression();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/assets"))
        {
            context.Context.Response.Headers.CacheControl =
                "public,max-age=31536000,immutable";
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapPost("/auth/login", async (
    LoginRequest request,
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrEmpty(request.Password))
        return Results.BadRequest(new { message = "請輸入帳號與密碼。" });

    try
    {
        var client = httpClientFactory.CreateClient("DDM");
        using var response = await client.PostAsJsonAsync("Login", request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Results.Json(new { message = "登入服務暫時無法使用。" }, statusCode: StatusCodes.Status502BadGateway);

        var loginResult = JsonSerializer.Deserialize<LoginEnvelope>(body, webJsonOptions);
        var account = loginResult?.Results.FirstOrDefault()?.Account?.Trim();

        if (loginResult?.Status != 1 || string.IsNullOrWhiteSpace(account))
        {
            return Results.Json(
                new { message = loginResult?.Description ?? "帳號或密碼錯誤。" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        await SignInAccountAsync(context, account);
        return Results.Ok(new { account });
    }
    catch (HttpRequestException)
    {
        return Results.Json(
            new { message = "無法連線到登入服務，請確認 API 與 DDM 已啟動。" },
            statusCode: StatusCodes.Status502BadGateway);
    }
})
.RequireRateLimiting("login");

app.MapPost("/auth/card-login", async (
    CardLoginRequest request,
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var cardUid = request.CardUid?.Trim();
    if (string.IsNullOrEmpty(cardUid) || cardUid.Length > 32)
        return Results.BadRequest(new { message = "無法辨識卡片內容，請重新感應。" });

    try
    {
        var client = httpClientFactory.CreateClient("DDM");
        using var response = await client.PostAsJsonAsync("CardLogin", new { cardUid }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Results.Json(new { message = "登入服務暫時無法使用。" }, statusCode: StatusCodes.Status502BadGateway);

        var loginResult = JsonSerializer.Deserialize<LoginEnvelope>(body, webJsonOptions);
        var account = loginResult?.Results.FirstOrDefault()?.Account?.Trim();

        if (loginResult?.Status != 1 || string.IsNullOrWhiteSpace(account))
        {
            return Results.Json(
                new { message = loginResult?.Description ?? "此卡片尚未綁定帳號。" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        await SignInAccountAsync(context, account);
        return Results.Ok(new { account });
    }
    catch (HttpRequestException)
    {
        return Results.Json(
            new { message = "無法連線到登入服務，請確認 API 與 DDM 已啟動。" },
            statusCode: StatusCodes.Status502BadGateway);
    }
})
.RequireRateLimiting("login");

app.MapGet("/auth/session", (ClaimsPrincipal user) =>
    Results.Ok(new { account = user.Identity!.Name }))
    .RequireAuthorization();

app.MapPost("/auth/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
})
.RequireAuthorization();

app.MapPost("/ddm/{endpoint}", async (
    string endpoint,
    HttpRequest incomingRequest,
    ClaimsPrincipal user,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    if (endpoint is not (
        "ReviseText" or
        "GetConversationSummaries" or
        "GetConversationById" or
        "SoftDeleteConversation" or
        "RestoreConversation" or
        "GetCards" or
        "BindCard" or
        "UnbindCard"))
        return Results.NotFound();

    try
    {
        var account = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(account))
            return Results.Unauthorized();

        using var reader = new StreamReader(incomingRequest.Body, Encoding.UTF8);
        var incomingBody = await reader.ReadToEndAsync(cancellationToken);
        var payload = JsonNode.Parse(incomingBody) as JsonObject;
        if (payload is null)
            return Results.BadRequest(new { message = "請求格式錯誤。" });

        payload["account"] = account;
        if (payload.ContainsKey("employeeId")) payload["employeeId"] = account;

        using var outgoingRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        var client = httpClientFactory.CreateClient("DDM");
        using var response = await client.SendAsync(outgoingRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";

        return Results.Content(
            body,
            mediaType,
            Encoding.UTF8,
            (int)response.StatusCode);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        // Browser refreshes and navigation abort the in-flight proxy request.
        // Treat that as a normal client disconnect instead of an application error.
        return Results.StatusCode(499);
    }
    catch (TaskCanceledException)
    {
        return Results.Problem(
            title: "DDM 回應逾時",
            detail: "請確認 DDM 服務已在設定的網址啟動。",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException exception)
    {
        return Results.Problem(
            title: "無法連線至 DDM",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
})
.RequireAuthorization();

app.MapFallbackToFile("index.html");

app.Run();

static async Task SignInAccountAsync(HttpContext context, string account)
{
    var identity = new ClaimsIdentity(
        new[] { new Claim(ClaimTypes.Name, account) },
        CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(identity),
        new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });
}

sealed record LoginRequest(string Account, string Password);

sealed record CardLoginRequest(string? CardUid);

sealed class LoginEnvelope
{
    public int Status { get; set; }
    public string? Description { get; set; }
    public List<LoginAccount> Results { get; set; } = [];
}

sealed class LoginAccount
{
    public string? Account { get; set; }
}
