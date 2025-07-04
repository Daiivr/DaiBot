using System;

namespace SysBot.Pokemon.Discord.Models;

public class UserStats
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Points { get; set; }
    public int XP { get; set; }
    public int Level { get; set; }
    public int HelloCount { get; set; }
    public DateTime LastXPGain { get; set; }
    public DateTime CooldownEnd { get; set; }
}
