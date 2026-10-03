using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Features.Health.Test;

namespace TemplateApp.Features.Health;

public partial class TestController
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        TestQuery query = new();
        ErrorOr<TestResponse> resultOrError = await _messageBus.InvokeAsync<ErrorOr<TestResponse>>(query, ct);
        return resultOrError.Match(Ok, Problem);
    }
}
