using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using TemplateApp.Infrastructure.Persistence;
using TemplateApp.Shared.Http;

namespace TemplateApp.Infrastructure.Http;

public sealed class IdempotencyFilter<TRequest> : IEndpointFilter
    where TRequest : notnull
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    private readonly TimeProvider _clock;
    private readonly JsonSerializerOptions _json;

    public IdempotencyFilter(TimeProvider clock, IOptions<JsonOptions> json)
    {
        _clock = clock;
        _json = json.Value.SerializerOptions;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(KeyHeader, out StringValues header))
            return await next(context);

        if (!TryParseKey(header, out Guid key))
            return ApiResults.Problem(HttpErrors.IdempotencyKeyInvalid);

        string owner = OwnerOf(http.User);
        byte[] fingerprint = Fingerprint(http.Request, context.Arguments.OfType<TRequest>().Single());
        AppDbContext db = http.RequestServices.GetRequiredService<AppDbContext>();
        CancellationToken ct = http.RequestAborted;

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);
        int inserted = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "IdempotencyKeys" ("OwnerId", "Key", "Fingerprint", "CreatedAt")
            VALUES ({owner}, {key}, {fingerprint}, {_clock.GetUtcNow()})
            ON CONFLICT ("OwnerId", "Key") DO NOTHING
            """,
            ct);

        if (inserted == 0)
            return await ReplayAsync(db, http, owner, key, fingerprint, ct);

        object? result = await next(context);
        if (result is not IResult handled)
            throw new InvalidOperationException("WithIdempotency needs a handler that returns IResult.");

        byte[] body = await RenderAsync(handled, http);
        HttpResponse response = http.Response;
        int status = response.StatusCode;
        if (status is >= 200 and <= 299)
        {
            string? location = response.Headers.Location;
            string? contentType = response.ContentType;
            await db.IdempotencyKeys
                .Where(k => k.OwnerId == owner && k.Key == key)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(k => k.StatusCode, status)
                        .SetProperty(k => k.Location, location)
                        .SetProperty(k => k.ContentType, contentType)
                        .SetProperty(k => k.Body, body),
                    ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            await transaction.RollbackAsync(ct);
        }

        await response.Body.WriteAsync(body, ct);
        return Results.Empty;
    }

    private static async Task<object?> ReplayAsync(
        AppDbContext db,
        HttpContext http,
        string owner,
        Guid key,
        byte[] fingerprint,
        CancellationToken ct)
    {
        IdempotencyKey stored = await db.IdempotencyKeys
            .AsNoTracking()
            .SingleAsync(k => k.OwnerId == owner && k.Key == key, ct);

        if (!stored.Fingerprint.AsSpan().SequenceEqual(fingerprint))
            return ApiResults.Problem(HttpErrors.IdempotencyKeyReused);

        if (stored.StatusCode is not int status || stored.Body is null)
            throw new InvalidOperationException("A committed idempotency key always carries its response.");

        HttpResponse response = http.Response;
        response.StatusCode = status;
        response.ContentType = stored.ContentType;
        if (stored.Location is not null)
            response.Headers.Location = stored.Location;
        response.Headers[ReplayedHeader] = "true";
        await response.Body.WriteAsync(stored.Body, ct);
        return Results.Empty;
    }

    private static async Task<byte[]> RenderAsync(IResult result, HttpContext http)
    {
        Stream original = http.Response.Body;
        using MemoryStream buffer = new();
        http.Response.Body = buffer;
        try
        {
            await result.ExecuteAsync(http);
        }
        finally
        {
            http.Response.Body = original;
        }

        return buffer.ToArray();
    }

    private static bool TryParseKey(StringValues header, out Guid key)
    {
        string raw = header.ToString();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            raw = raw[1..^1];

        return Guid.TryParseExact(raw, "D", out key);
    }

    private static string OwnerOf(ClaimsPrincipal user)
    {
        string? id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("The session is missing the caller id claim.");

        return id;
    }

    private byte[] Fingerprint(HttpRequest request, TRequest payload)
    {
        byte[] target = Encoding.UTF8.GetBytes($"{request.Method} {request.Path}\n");
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, _json);
        return SHA256.HashData([.. target, .. body]);
    }
}
