using Discord;
using Discord.Commands;
using Discord.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public class HelpModule(CommandService commandService) : ModuleBase<SocketCommandContext>
{
#pragma warning disable CS9124 // Parameter is captured into the state of the enclosing type and its value is also used to initialize a field, property, or event.
    private readonly CommandService _commandService = commandService;
#pragma warning restore CS9124 // Parameter is captured into the state of the enclosing type and its value is also used to initialize a field, property, or event.

    [Command("help")]
    [Summary("Muestra los comandos disponibles.")]
    public async Task HelpAsync()
    {
        var builder = new EmbedBuilder
        {
            Color = new Color(114, 137, 218),
            Description = "📝 Estos son los comandos que puedes usar:",
        };

        var botPrefix = SysCordSettings.HubConfig.Discord.CommandPrefix;
        var mgr = SysCordSettings.Manager;
        var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
        var owner = app.Owner.Id;
        var uid = Context.User.Id;

        int fieldsCount = 0; // Variable para realizar un seguimiento del número de campos agregados

        foreach (var module in commandService.Modules)
        {
            string? description = null;
            HashSet<string> mentioned = new HashSet<string>(); // Corregir la inicialización de HashSet

            foreach (var cmd in module.Commands)
            {
                var name = cmd.Name;
                if (mentioned.Contains(name))
                    continue;
                if (cmd.Attributes.Any(z => z is RequireOwnerAttribute) && owner != uid)
                    continue;
                if (cmd.Attributes.Any(z => z is RequireSudoAttribute) && !mgr.CanUseSudo(uid))
                    continue;

                mentioned.Add(name);
                var result = await cmd.CheckPreconditionsAsync(Context).ConfigureAwait(false);
                if (result.IsSuccess)
                    description += $"- {cmd.Aliases[0]}\n";
            }
            if (string.IsNullOrWhiteSpace(description))
                continue;

            var moduleName = module.Name;
            var gen = moduleName.IndexOf('`');
            if (gen != -1)
                moduleName = moduleName[..gen];

            builder.AddField(x =>
            {
                x.Name = moduleName;
                x.Value = description;
                x.IsInline = true;
            });

            fieldsCount++; // Incrementar el contador de campos agregados

            // Si el número de campos agregados alcanza 25, enviar el EmbedBuilder actual y crear uno nuevo
            if (fieldsCount >= 25)
            {
                await Context.User.SendMessageAsync(embed: builder.Build()).ConfigureAwait(false);
                builder = new EmbedBuilder
                {
                    Color = new Color(114, 137, 218),
                    Title = "Continuación de la lista de comandos:", // Agregar título al nuevo EmbedBuilder
                };
                fieldsCount = 0; // Restablecer el contador de campos
            }
        }
        // Aquí agregas el footer antes de enviar la respuesta
        builder.Footer = new EmbedFooterBuilder
        {
            Text = $"Si necesitas ayuda sobre un comando especifico utiliza `{botPrefix}help` seguido del comando del que necesitas ayuda.",
            IconUrl = "https://i.imgur.com/gUstNQ8.gif"
        };

        // Enviar el último EmbedBuilder si hay campos restantes
        if (fieldsCount > 0)
        {
            await Context.Message.DeleteAsync();
            await Context.User.SendMessageAsync(embed: builder.Build()).ConfigureAwait(false);
            var reply = await ReplyAsync($"✅ {Context.User.Mention}, la información de ayuda ha sido enviada a tu MD. Por favor, revisa tus mensajes directos.");
            await Task.Delay(10000); // Delay de 10 segundos
            await reply.DeleteAsync(); // Elimina el mensaje de respuesta del bot
        }
    }


    [Command("help")]
    [Summary("Muestra información sobre un comando específico.")]
    public async Task HelpAsync([Summary("The command to get information for.")] string command)
    {
        // Verificar si el comando se está ejecutando en un MD
        if (!(Context.Channel is IDMChannel))
        {
            var reply = await ReplyAsync($"⚠️ Lo siento {Context.User.Mention}, este comando solo puede ser usado en el MD del bot.");

            // Verificar si el contexto NO es un MD, y luego eliminar el mensaje del usuario
            if (Context.Channel is IGuildChannel) // Verifica si es un canal dentro de un servidor
            {
                await Context.Message.DeleteAsync(); // Elimina el mensaje del usuario
            }

            // Esperar 10 segundos antes de eliminar el mensaje de respuesta del bot
            await Task.Delay(10000); // Delay de 10 segundos
            await reply.DeleteAsync(); // Elimina el mensaje de respuesta del bot

            return;
        }

        var searchResult = _commandService.Search(Context, command);

        if (!searchResult.IsSuccess)
        {
            await ReplyAsync($"⚠️ Lo siento, no pude encontrar un comando como **{command}**.");
            return;
        }

        var embedBuilder = new EmbedBuilder()
            .WithTitle($"Ayuda para el comanod: {command}")
            .WithColor(Color.Blue);

        foreach (var match in searchResult.Commands)
        {
            var cmd = match.Command;

            var parameters = cmd.Parameters.Select(p => $"`{p.Name}` - {p.Summary}");
            var parameterSummary = string.Join("\n", parameters);

            embedBuilder.AddField(cmd.Name, $"{cmd.Summary}\n\n**Parámetros:**\n{parameterSummary}", false);
        }

        try
        {
            var dmChannel = await Context.User.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(embed: embedBuilder.Build());
        }
        catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.Forbidden)
        {
            await ReplyAsync($"⚠️ {Context.User.Mention}, no pude enviarte un mensaje privado porque tienes los MD desactivados. Por favor, habilítalos e inténtalo de nuevo.");
        }
        catch (Exception ex)
        {
            await ReplyAsync($"❌ Ocurrió un error: {ex.Message}");
        }
    }
}
