using Npgsql;
using YuChat.Infrastructure.Options;

namespace YuChat.Api.Tests;

public sealed class ConnectionStringNormalizerTests
{
    [Fact]
    public void Heroku_uri_is_converted_to_an_npgsql_connection_string()
    {
        var normalized = ConnectionStringNormalizer.Normalize(
            "postgres://user:secret@ec2-1-2-3-4.compute-1.amazonaws.com:5432/dbname");

        var builder = new NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("ec2-1-2-3-4.compute-1.amazonaws.com", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("dbname", builder.Database);
        Assert.Equal("user", builder.Username);
        Assert.Equal("secret", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
        Assert.True(builder.Pooling);
    }

    [Fact]
    public void Explicit_pool_size_overrides_the_default_for_uri_style_urls()
    {
        var normalized = ConnectionStringNormalizer.Normalize(
            "postgresql://user:secret@localhost:5432/dbname",
            maxPoolSize: 8);

        Assert.Equal(8, new NpgsqlConnectionStringBuilder(normalized).MaxPoolSize);
    }

    [Fact]
    public void Explicit_pool_size_overrides_the_default_for_key_value_strings()
    {
        var normalized = ConnectionStringNormalizer.Normalize(
            "Host=localhost;Database=dbname;Username=user;Password=secret;Maximum Pool Size=50",
            maxPoolSize: 8);

        Assert.Equal(8, new NpgsqlConnectionStringBuilder(normalized).MaxPoolSize);
    }

    [Fact]
    public void Key_value_strings_keep_their_own_pool_size_when_none_is_configured()
    {
        var normalized = ConnectionStringNormalizer.Normalize(
            "Host=localhost;Database=dbname;Username=user;Password=secret;Maximum Pool Size=5");

        Assert.Equal(5, new NpgsqlConnectionStringBuilder(normalized).MaxPoolSize);
    }

    [Fact]
    public void Sslmode_disable_in_the_url_is_honoured()
    {
        var normalized = ConnectionStringNormalizer.Normalize(
            "postgres://user:secret@localhost:5432/dbname?sslmode=disable");

        Assert.Equal(SslMode.Disable, new NpgsqlConnectionStringBuilder(normalized).SslMode);
    }

    [Fact]
    public void Empty_connection_string_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => ConnectionStringNormalizer.Normalize("  "));
    }
}
