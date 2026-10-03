using Microsoft.AspNetCore.Mvc;
using TemplateApp.Infrastructure.Common;
using Wolverine;

namespace TemplateApp.Features.Auth;

[Route("api/v1/auth")]
[Tags("Auth")]
public partial class AuthController : ApiController
{
    private readonly IMessageBus _messageBus;

    public AuthController(IMessageBus messageBus) => _messageBus = messageBus;
}
