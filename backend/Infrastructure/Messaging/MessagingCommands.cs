using JasperFx;

namespace TemplateApp.Infrastructure.Messaging;

public static class MessagingCommands
{
    private const string CodegenCommand = "codegen";

    public static async Task<int> RunWithMessagingCommandsAsync(this WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length > 0 && args[0] == CodegenCommand)
            return await app.RunJasperFxCommands(args);

        await app.RunAsync();
        return 0;
    }
}
