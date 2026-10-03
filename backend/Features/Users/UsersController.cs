using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Infrastructure.Common;
using Wolverine;

namespace TemplateApp.Features.Users;

[Route("api/v1/users")]
[Authorize]
public sealed partial class UsersController : ApiController
{
    private readonly IMessageBus _bus;

    public UsersController(IMessageBus bus)
    {
        _bus = bus;
    }
}
