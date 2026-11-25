using FluentValidation;
using TemplateApp.Shared.Http;

namespace TemplateApp.Features.Projects.ListProjects;

internal sealed class ListProjectsQueryValidator : AbstractValidator<ListProjectsQuery>
{
    public ListProjectsQueryValidator()
    {
        RuleFor(x => x.PageSize).ValidPageSize();
        RuleFor(x => x.Cursor).ValidCursor();
    }
}
