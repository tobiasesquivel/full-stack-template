using Microsoft.Extensions.Options;

namespace TemplateApp.Features.Users.OrchestrateAccountClosure;

[OptionsValidator]
internal sealed partial class AccountClosureOptionsValidator : IValidateOptions<AccountClosureOptions>;
