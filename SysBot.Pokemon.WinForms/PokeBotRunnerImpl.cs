using PKHeX.Core;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Twitch;
using SysBot.Pokemon.WinForms;
using SysBot.Pokemon.YouTube;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

/// <summary>
/// Bot Environment implementation with Integrations added.
/// </summary>
public class PokeBotRunnerImpl<T> : PokeBotRunner<T> where T : PKM, new()
{
    private YouTubeBot<T>? YouTube;
    private static TwitchBot<T>? Twitch;
    private readonly ProgramConfig _config;

    public PokeBotRunnerImpl(PokeTradeHub<T> hub, BotFactory<T> fac, ProgramConfig config) : base(hub, fac)
    {
        _config = config;
    }

    public PokeBotRunnerImpl(PokeTradeHubConfig config, BotFactory<T> fac, ProgramConfig programConfig) : base(config, fac)
    {
        _config = programConfig;
    }

    protected override void AddIntegrations()
    {
        AddDiscordBot(Hub.Config.Discord.Token);
        AddTwitchBot(Hub.Config.Twitch);
        AddYouTubeBot(Hub.Config.YouTube);
    }

    private void AddDiscordBot(string apiToken)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
            return;
        var bot = new SysCord<T>(this, _config);
        Task.Run(() => bot.MainAsync(apiToken, CancellationToken.None));
    }

    private void AddTwitchBot(TwitchSettings config)
    {
        if (string.IsNullOrWhiteSpace(config.Token))
            return;
        if (Twitch != null)
            return; // already created

        if (string.IsNullOrWhiteSpace(config.Channel))
            return;
        if (string.IsNullOrWhiteSpace(config.Username))
            return;
        if (string.IsNullOrWhiteSpace(config.Token))
            return;

        Twitch = new TwitchBot<T>(Hub.Config.Twitch, Hub);
        if (Hub.Config.Twitch.DistributionCountDown)
            Hub.BotSync.BarrierReleasingActions.Add(() => Twitch.StartingDistribution(config.MessageStart));
    }

    private void AddYouTubeBot(YouTubeSettings config)
    {
        if (string.IsNullOrWhiteSpace(config.ClientID))
            return;
        if (YouTube != null)
            return; // already created

        WinFormsUtil.Alert("Inicie sesión con su navegador");
        if (string.IsNullOrWhiteSpace(config.ChannelID))
            return;
        if (string.IsNullOrWhiteSpace(config.ClientID))
            return;
        if (string.IsNullOrWhiteSpace(config.ClientSecret))
            return;

        YouTube = new YouTubeBot<T>(Hub.Config.YouTube, Hub);
        Hub.BotSync.BarrierReleasingActions.Add(() => YouTube.StartingDistribution(config.MessageStart));
    }
}
