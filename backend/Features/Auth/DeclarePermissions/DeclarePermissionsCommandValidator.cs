using FluentValidation;
using TemplateApp.Features.Auth.Access;
using TemplateApp.Infrastructure.Access;
using TemplateApp.Infrastructure.Hosting;

namespace TemplateApp.Features.Auth.DeclarePermissions;

internal sealed class DeclarePermissionsCommandValidator : AbstractValidator<DeclarePermissionsCommand>
{
    public DeclarePermissionsCommandValidator(HostFeatures local)
    {
        RuleFor(x => x).Custom((command, context) =>
        {
            if (local.Names.Contains(command.Feature, StringComparer.Ordinal))
            {
                context.AddFailure(nameof(command.Feature), $"Feature '{command.Feature}' runs in this process: its permissions come from its own declaration, not from another process.");
                return;
            }

            foreach (string problem in PermissionDeclarationRules.Problems(new FeaturePermissions(command.Feature, command.Grants)))
                context.AddFailure(nameof(command.Grants), problem);
        });
    }
}
