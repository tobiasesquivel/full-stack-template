using ErrorOr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Domain.Authorization;
using TemplateApp.Features.Users.GetById;
using TemplateApp.Infrastructure.Authorization;

namespace TemplateApp.Features.Users;

public sealed partial class UsersController
{
    [HttpGet("{authId}", Name = "GetUserById")]
    [HasPermission(AppPermissions.Users.Read)]
    public async Task<IActionResult> GetUserById(
        string authId,
        CancellationToken ct)
    {
        ErrorOr<GetUserByIdQueryResult> result = await _bus.InvokeAsync<ErrorOr<GetUserByIdQueryResult>>(
            new GetUserByIdQuery(authId),
            ct);

        return result.Match(Ok, Problem);
    }
}
