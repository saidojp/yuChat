using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using YuChat.Data;
using YuChat.Domain;
using YuChat.Features.Chats;
using YuChat.Features.Friends;
using YuChat.Features.Notifications;
using YuChat.Features.Users;
using YuChat.Infrastructure.Encryption;
using YuChat.Infrastructure.Errors;
using YuChat.Infrastructure.Options;
using YuChat.Infrastructure.Security;
using YuChat.Realtime;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace YuChat.Api.Tests;

/// <summary>Wires the real services against an isolated in-memory SQLite database.</summary>
internal sealed class TestHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private TestHarness(SqliteConnection connection, ServiceProvider provider)
    {
        _connection = connection;
        Provider = provider;
    }

    public ServiceProvider Provider { get; }

    public static async Task<TestHarness> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();

        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));

        services.AddSingleton<IMessageEncryptionService>(new AesGcmMessageEncryptionService(Options.Create(new EncryptionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ActiveKeyVersion = 1,
        })));

        services.AddSingleton<IHubContext<ChatHub>>(new NoopHubContext());
        services.AddLogging();
        services.AddSingleton<RealtimeNotifier>();
        services.AddScoped<ChatAccessService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<UserService>();
        services.AddScoped<FriendService>();
        services.AddScoped<ChatService>();

        var provider = services.BuildServiceProvider();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        return new TestHarness(connection, provider);
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await action(db);
    }

    public async Task<User> CreateUserAsync(string login, string? handle = null)
    {
        return await WithDbAsync(async db =>
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                Login = login,
                LoginNormalized = login.ToLowerInvariant(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123", 4),
                UserHandle = handle,
                UserHandleNormalized = handle?.ToLowerInvariant(),
                CreatedAt = DateTime.UtcNow,
            };

            db.Users.Add(user);
            await db.SaveChangesAsync();
            return user;
        });
    }

    public async Task<FriendService> FriendServiceAsync()
    {
        var scope = Provider.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<FriendService>();
    }

    public async Task<ChatService> ChatServiceAsync()
    {
        var scope = Provider.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<ChatService>();
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private sealed class NoopHubContext : IHubContext<ChatHub>
    {
        public IHubClients Clients { get; } = new NoopClients();
        public IGroupManager Groups { get; } = new NoopGroups();
    }

    private sealed class NoopClients : IHubClients
    {
        private static readonly IClientProxy Proxy = new NoopProxy();

        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    private sealed class NoopProxy : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopGroups : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
