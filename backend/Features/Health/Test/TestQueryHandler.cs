using ErrorOr;

namespace TemplateApp.Features.Health.Test;

public sealed class TestQueryHandler
{
    public Task<ErrorOr<TestResponse>> Handle(TestQuery query, CancellationToken ct)
    {
        return Task.FromResult<ErrorOr<TestResponse>>(new TestResponse("Healthy", DateTime.UtcNow));
    }
}
