using FluentValidation;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Tasks.ListTasks;

internal sealed class ListTasksQueryValidator : AbstractValidator<ListTasksQuery>
{
    public ListTasksQueryValidator()
    {
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Cursor).ValidCursor();
    }
}
