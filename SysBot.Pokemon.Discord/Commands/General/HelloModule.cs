using Discord;
using Discord.Commands;
using ImageSharp = SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Net.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.Security.Cryptography;

namespace SysBot.Pokemon.Discord;

public class HelloModule : ModuleBase<SocketCommandContext>
{
    private static readonly string[] Greetings = new[]
    {
        "¡Hola", "¡Hey", "¡Holi", "¡Saludos", "¡Qué tal"
    };

    private static readonly string[] WelcomeMessages = new[]
    {
        "me alegra verte", "qué gusto verte", "es bueno tenerte por aquí", "encantado de verte", "bienvenido nuevamente"
    };

    private static readonly string[] Gifs = new[]
    {
        "https://i.pinimg.com/originals/1a/0e/2f/1a0e2f953f778092b079dcf6f5800b5d.gif",
        "https://media.giphy.com/media/v1.Y2lkPTc5MGI3NjExbzEyajExNzQ1cGF6NGtkMXJkNjJoZWNsa3c4Y2dyNHRha3J1bTd5cCZlcD12MV9naWZzX3NlYXJjaCZjdD1n/XcHrYdvA1RWrs/giphy.gif",
        "https://media.giphy.com/media/l3vR85PnGsBwu1PFK/giphy.gif",
        "https://media.giphy.com/media/v1.Y2lkPTc5MGI3NjExbWVwbHVvdmR0ZHFmZ2l2dzA1YWRocjBvNGU2ODU5Zms3bTQ2d3ZtYyZlcD12MV9naWZzX3NlYXJjaCZjdD1n/ASd0Ukj0y3qMM/giphy.gif",
        "https://media.giphy.com/media/v1.Y2lkPTc5MGI3NjExanJtbmpqZjA4azR3M2h4Mms4aGszNDFsbnY4eDB2ZmllZHg5eDR1diZlcD12MV9naWZzX3NlYXJjaCZjdD1n/OkJat1YNdoD3W/giphy.gif"
    };

    [Command("hello")]
    [Alias("hi")]
    [Summary("Saluda al bot y obtén una respuesta.")]
    public async Task PingAsync()
    {
        var avatarUrl = Context.User.GetAvatarUrl(size: 128) ?? Context.User.GetDefaultAvatarUrl();
        var color = await GetDominantColorAsync(avatarUrl);

        var greeting = GetTimeBasedGreeting();
        var intro = PickRandom(Greetings);
        var welcome = PickRandom(WelcomeMessages);
        var gif = PickRandom(Gifs);

        string format = SysCordSettings.Settings.HelloResponse ?? "¡{0}!";
        var message = string.Format(format, Context.User.Mention);

        var embed = new EmbedBuilder()
            .WithTitle($"{intro} 👋")
            .WithDescription($"{message} {greeting}, {welcome}.")
            .WithColor(color)
            .WithCurrentTimestamp()
            .WithThumbnailUrl("https://i.imgur.com/BcMI5KC.png")
            .WithImageUrl(gif)
            .WithFooter(footer =>
            {
                footer.WithText($"Solicitado por {Context.User.Username}");
                footer.WithIconUrl(avatarUrl);
            });

        await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
    }

    private static string PickRandom(string[] array)
    {
        return array[RandomNumberGenerator.GetInt32(array.Length)];
    }

    private static string GetTimeBasedGreeting()
    {
        var hour = DateTime.Now.Hour;

        return hour switch
        {
            < 12 => "¡Buenos días",
            < 18 => "¡Buenas tardes",
            _ => "¡Buenas noches"
        };
    }

    private async Task<Color> GetDominantColorAsync(string imageUrl)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(imageUrl);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var image = ImageSharp.Image.Load<Rgba32>(stream);

        var histogram = new Dictionary<(int R, int G, int B), int>();

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                var key = (pixel.R / 10 * 10, pixel.G / 10 * 10, pixel.B / 10 * 10);
                histogram.TryGetValue(key, out int count);
                histogram[key] = count + 1;
            }
        }

        var dominant = histogram.OrderByDescending(k => k.Value).First().Key;
        return new Color(dominant.R, dominant.G, dominant.B);
    }
}
