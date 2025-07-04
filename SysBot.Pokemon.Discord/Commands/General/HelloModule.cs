// HelloModule.cs actualizado completamente
using Discord;
using Discord.Commands;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using System.Net.Http;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System;

using DiscordColor = Discord.Color;
using ImageSharpColor = SixLabors.ImageSharp.Color;
using ImageSharpImage = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.Drawing;
using SysBot.Pokemon.Discord.Models;

namespace SysBot.Pokemon.Discord;

public class HelloModule : ModuleBase<SocketCommandContext>
{
    private const string StatsFilePath = "user_stats.json";
    private const string BackgroundImagePath = "Assets/hi_background.png";

    private static readonly string[] Emojis = new[]
    {
        "👋", "🙌", "😊", "✨", "🎉", "🌟", "😄", "😎"
    };

    private static readonly string[] Welcomes = new[]
    {
        "¡Qué gusto verte por aquí",
        "¡Encantado de verte de nuevo",
        "¡Siempre es un placer saludarte",
        "¡Me alegra verte conectado",
        "¡Qué bueno tenerte de vuelta",
        "¡Espero que estés teniendo un gran día",
        "¡Listo para más aventuras",
        "¡Qué sorpresa verte por aquí",
        "¡Hola hola, ¿cómo va todo",
        "¡Qué alegría tenerte por aquí"
    };

    [Command("hello")]
    [Alias("hi")]
    [Summary("Saluda al bot y obtén una respuesta.")]
    public async Task HelloAsync([Remainder] string args = "")
    {
        try
        {
            await Context.Channel.TriggerTypingAsync();
            await Context.Message.AddReactionAsync(new Emoji("👋"));

            var userId = Context.User.Id.ToString();
            var avatarUrl = Context.User.GetAvatarUrl(size: 128) ?? Context.User.GetDefaultAvatarUrl();
            var color = await GetDominantColorAsync(avatarUrl);

            var welcomes = Welcomes;

            var hour = (DateTime.UtcNow.Hour - 4 + 24) % 24;
            var greeting = hour switch
            {
                < 12 => "¡Buenos días",
                < 18 => "¡Buenas tardes",
                _ => "¡Buenas noches"
            };

            var emoji = GetRandomItem(Emojis);
            var welcome = GetRandomItem(welcomes);

            var stats = LoadOrCreateStats();
            if (!stats.ContainsKey(userId))
                stats[userId] = new UserStats();

            stats[userId].HelloCount++;
            var count = stats[userId].HelloCount;

            SaveStats(stats);

            var responseText = SysCordSettings.Settings.HelloResponse;
            var formattedMsg = string.Format(responseText, Context.User.Mention);

            var embed = new EmbedBuilder()
                .WithTitle($"{greeting} {emoji}")
                .WithDescription($"{formattedMsg}\n{welcome}!")
                .WithColor(color)
                .WithCurrentTimestamp();

            embed.WithFooter(footer =>
            {
                footer.WithText($"Has saludado al bot {count} {(count == 1 ? "vez" : "veces")}");
                footer.WithIconUrl(avatarUrl);
            });

            var imageStream = await GenerateGreetingImageAsync(avatarUrl);
            var file = new FileAttachment(imageStream, "greeting.png");
            embed.WithImageUrl("attachment://greeting.png");

            await Context.Channel.SendFileAsync(file.Stream, file.FileName, embed: embed.Build());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en HelloAsync: {ex.Message}");
        }
    }

    private async Task<DiscordColor> GetDominantColorAsync(string imageUrl)
    {
        using var client = new HttpClient();
        using var response = await client.GetAsync(imageUrl);
        using var stream = await response.Content.ReadAsStreamAsync();
        using var image = ImageSharpImage.Load<Rgba32>(stream);

        var histogram = new Dictionary<Rgba32, int>();
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (histogram.ContainsKey(pixel))
                    histogram[pixel]++;
                else
                    histogram[pixel] = 1;
            }
        }

        var dominant = histogram.OrderByDescending(kvp => kvp.Value).First().Key;
        return new DiscordColor(dominant.R, dominant.G, dominant.B);
    }

    private static string GetRandomItem(string[] array)
    {
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);
        var index = Math.Abs(BitConverter.ToInt32(bytes, 0)) % array.Length;
        return array[index];
    }

    private async Task<Stream> GenerateGreetingImageAsync(string avatarUrl)
    {
        var assembly = typeof(HelloModule).Assembly;
        using var stream = assembly.GetManifestResourceStream("SysBot.Pokemon.Discord.Assets.hi_background.png");
        if (stream == null)
            throw new Exception("No se pudo cargar imagen incrustada.");
        using var background = ImageSharpImage.Load<Rgba32>(stream);

        using var client = new HttpClient();
        using var avatarResponse = await client.GetAsync(avatarUrl);
        using var avatarStream = await avatarResponse.Content.ReadAsStreamAsync();
        using var avatarImage = ImageSharpImage.Load<Rgba32>(avatarStream);

        // Set avatar size
        int avatarSize = 205;
        avatarImage.Mutate(x => x.Resize(avatarSize, avatarSize));
        avatarImage.ApplyRoundedCorners(avatarSize / 2f); // Apply circle crop

        // Adjusted position to center it on Snorlax's face
        var position = new Point(700, 465);

        background.Mutate(ctx =>
        {
            ctx.DrawImage(avatarImage, position, 1f);
        });

        var output = new MemoryStream();
        await background.SaveAsPngAsync(output);
        output.Position = 0;
        return output;
    }

    private Dictionary<string, UserStats> LoadOrCreateStats()
    {
        if (!File.Exists(StatsFilePath))
        {
            var emptyStats = new Dictionary<string, UserStats>();
            SaveStats(emptyStats);
            return emptyStats;
        }

        var json = File.ReadAllText(StatsFilePath);
        return JsonConvert.DeserializeObject<Dictionary<string, UserStats>>(json) ?? new Dictionary<string, UserStats>();
    }

    private void SaveStats(Dictionary<string, UserStats> stats)
    {
        var json = JsonConvert.SerializeObject(stats, Formatting.Indented);
        File.WriteAllText(StatsFilePath, json);
    }
}

public static class ImageExtensions
{
    public static void ApplyRoundedCorners(this Image<Rgba32> image, float radius)
    {
        int width = image.Width;
        int height = image.Height;

        var mask = new Image<Rgba32>(width, height);
        mask.Mutate(ctx =>
        {
            var circle = new EllipsePolygon(width / 2f, height / 2f, radius);
            ctx.Fill(ImageSharpColor.White, circle);
        });

        image.Mutate(ctx =>
        {
            ctx.DrawImage(mask, new GraphicsOptions
            {
                Antialias = true,
                AlphaCompositionMode = PixelAlphaCompositionMode.DestIn,
                BlendPercentage = 1.0f
            });
        });
    }
}
