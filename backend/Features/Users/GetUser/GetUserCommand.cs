namespace TemplateApp.Features.Users.GetUser;

internal sealed record GetUserCommand(string CallerId, string AuthId);
