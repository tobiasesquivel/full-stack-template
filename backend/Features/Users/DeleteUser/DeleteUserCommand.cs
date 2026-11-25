namespace TemplateApp.Features.Users.DeleteUser;

internal sealed partial record DeleteUserCommand(string CallerId, string AuthId);
