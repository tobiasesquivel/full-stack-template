using TemplateApp.Features.Tasks.Access;

namespace TemplateApp.Features.Tasks.GetTask;

internal sealed partial record GetTaskQuery(Caller Caller, Guid Id);
