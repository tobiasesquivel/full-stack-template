using Microsoft.AspNetCore.Mvc;
using TemplateApp.Infrastructure.Common;
using Wolverine;

namespace TemplateApp.Features.Health;

[Route("api/v1/health")]
public partial class TestController : ApiController
{
    private readonly IMessageBus _messageBus;

    public TestController(IMessageBus messageBus) => _messageBus = messageBus;
}
