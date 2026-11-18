using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using YuChat.Data;
using YuChat.Features;
using YuChat.Features.Auth;
using YuChat.Features.Chats;
using YuChat.Features.Friends;
using YuChat.Features.Notifications;
using YuChat.Features.Users;
using YuChat.Infrastructure.Encryption;
using YuChat.Infrastructure.Errors;
using YuChat.Infrastructure.Options;
using YuChat.Infrastructure.Security;
using YuChat.Realtime;
using YuChat.Seed;

var builder = WebApplication.CreateBuilder(args);

// Local development convenience: <contentRoot>/.env overrides appsettings in Development only.
// In every other environment configuration comes exclusively from real environment variables,
// so a stray .env file can never influence a deployment.
if (builder.Environment.IsDevelopment())
{
    var envFile = Path.Combine(builder.Environment.ContentRootPath, ".env");

    if (File.Exists(envFile))
    {
        var values = DotNetEnv.Env.Load(envFile, new DotNetEnv.LoadOptions(setEnvVars: false));

        // .env uses the shell convention (Section__Key); configuration needs Section:Key.
        builder.Configuration.AddInMemoryCollection(
            values.ToDictionary(
                pair => pair.Key.Replace("__", ":", StringComparison.Ordinal),
                pair => (string?)pair.Value));
    }
}

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.SectionName).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptions.Validator>();

builder.Services.AddOptions<EncryptionOptions>().BindConfiguration(EncryptionOptions.SectionName).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptions.Validator>();

builder.Services.AddOptions<FrontendOptions>().BindConfiguration(FrontendOptions.SectionName);

var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

var connectionString = ConnectionStringNormalizer.Normalize(rawConnectionString);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, npgsql =>
    {
        npgsql.EnableRetryOnFailure(3);
        npgsql.CommandTimeout(30);
    });

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging(false);
        options.EnableDetailedErrors();
    }
});

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 16 * 1024;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    // Browsers throttle timers in background tabs, so the server must be patient before
    // declaring a client gone; otherwise switching tabs drops the connection.
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
});

builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>();
builder.Services.AddSingleton<PresenceTracker>();
builder.Services.AddSingleton<IMessageEncryptionService, AesGcmMessageEncryptionService>();
builder.Services.AddSingleton<RealtimeNotifier>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ChatAccessService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<FriendService>();
builder.Services.AddScoped<ChatService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddAppRateLimiting();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.CreateKey(jwt.Key),
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // SignalR cannot set Authorization headers on WebSockets, so the token is passed
                // in the query string. Accept it ONLY for the hub path.
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

var frontendOrigin = builder.Configuration.GetSection(FrontendOptions.SectionName).Get<FrontendOptions>()?.Origin ?? "http://localhost:5173";

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(frontendOrigin)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// Enums cross the wire as readable strings (ProblemDetails codes stay stable for clients).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.Configure<JsonHubProtocolOptions>(options =>
{
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow })).AllowAnonymous();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapFriendEndpoints();
app.MapChatEndpoints();
app.MapNotificationEndpoints();
app.MapHub<ChatHub>("/hubs/chat");

if (args.Contains("--seed"))
{
    await SeedData.RunAsync(app.Services);
    return;
}

app.Run();

public partial class Program;
