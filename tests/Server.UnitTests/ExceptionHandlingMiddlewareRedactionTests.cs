using GameNet.Server.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ExceptionHandlingMiddlewareRedactionTests
{
    [Fact]
    public async Task Unhandled_exception_secret_is_not_written_to_response_or_logs()
    {
        const string secret = "db-password-sentinel-do-not-log";
        var logger = new CapturingLogger<ExceptionHandlingMiddleware>();
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("Database connection failed; Password=" + secret),
            logger);
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        responseBody.Position = 0;
        using var reader = new StreamReader(responseBody);
        var response = await reader.ReadToEndAsync();
        var logs = string.Join(Environment.NewLine, logger.Messages);

        Assert.DoesNotContain(secret, response);
        Assert.DoesNotContain(secret, logs);
        Assert.Contains(nameof(InvalidOperationException), logs);
        Assert.DoesNotContain(logger.Exceptions, exception => exception is not null);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();
        public void Dispose() { }
    }
}
