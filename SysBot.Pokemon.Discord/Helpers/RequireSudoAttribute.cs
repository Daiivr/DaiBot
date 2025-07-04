using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public sealed class RequireSudoAttribute : PreconditionAttribute
{
    // Override the CheckPermissions method
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var mgr = SysCordSettings.Manager;
        if (mgr.Config.AllowGlobalSudo && mgr.CanUseSudo(context.User.Id))
            return Task.FromResult(PreconditionResult.FromSuccess());

        // Check if this user is a Guild User, which is the only context where roles exist
        if (context.User is not SocketGuildUser gUser)
            return Task.FromResult(PreconditionResult.FromError($"⚠️ Lo siento {context.User.Mention}, este comando solo puede ser usado dentro de un servidor y no en mensajes directos."));

        if (mgr.CanUseSudo(gUser.Roles.Select(z => z.Name)))
            return Task.FromResult(PreconditionResult.FromSuccess());

        // Since it wasn't, fail
        return Task.FromResult(PreconditionResult.FromError($"❌ {context.User.Mention} no estás autorizado a ejecutar este comando."));
    }
}
