using Discord.Commands;
using System;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public sealed class RequireOwnerAttribute : PreconditionAttribute
{
    // Override the CheckPermissions method and mark it as async
    public override async Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        // Get the bot's application info to access the owner information
        var application = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);

        // Check if the user executing the command is the bot owner
        if (context.User.Id == application.Owner.Id)
            return PreconditionResult.FromSuccess();

        // If the user is not the owner, return an error
        return PreconditionResult.FromError($"⚠️ {context.User.Mention} solo el dueño del bot puede ejecutar este comando.");
    }
}
