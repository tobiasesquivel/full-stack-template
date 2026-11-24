namespace TemplateApp.Features.Users.DeleteUser;

internal sealed record DeleteUserCommand(string CallerId, string AuthId);
