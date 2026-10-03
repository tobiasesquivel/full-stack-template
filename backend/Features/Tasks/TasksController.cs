using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TemplateApp.Infrastructure.Common;
using Wolverine;

namespace TemplateApp.Features.Tasks;

[Route("api/v1/tasks")]
[Authorize]
public sealed partial class TasksController : ApiController
{
    private readonly IMessageBus _bus;

    public TasksController(IMessageBus bus)
    {
        _bus = bus;
    }
}
