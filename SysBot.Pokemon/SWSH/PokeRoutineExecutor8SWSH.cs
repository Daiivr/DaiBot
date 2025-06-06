using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsSWSH;

namespace SysBot.Pokemon;

/// <summary>
/// Executor for SW/SH games.
/// </summary>
public abstract class PokeRoutineExecutor8SWSH(PokeBotState Config) : PokeRoutineExecutor<PK8>(Config)
{
    protected PokeDataOffsetsSWSH Offsets { get; } = new();

    public async Task<bool> CheckIfSoftBanned(CancellationToken token)
    {
        // Check if the Unix Timestamp isn't zero, if so we are soft-banned.
        var data = await Connection.ReadBytesAsync(SoftBanUnixTimespanOffset, 1, token).ConfigureAwait(false);
        return data[0] > 1;
    }

    public async Task CleanExit(CancellationToken token)
    {
        await SetScreen(ScreenState.On, token).ConfigureAwait(false);
        Log("Desconexión de controladores al salir de rutina.");
        await DetachController(token).ConfigureAwait(false);
    }

    public async Task CloseGame(PokeTradeHubConfig config, CancellationToken token)
    {
        var timing = config.Timings;

        // Close out of the game
        await Click(B, 0_500, token).ConfigureAwait(false);
        await Click(HOME, 2_000 + timing.ClosingGameSettings.ExtraTimeReturnHome, token).ConfigureAwait(false);
        await Click(X, 1_000, token).ConfigureAwait(false);
        await Click(A, 5_000 + timing.ClosingGameSettings.ExtraTimeCloseGame, token).ConfigureAwait(false);
        Log("Cerre el juego!");
    }

    public async Task EnsureConnectedToYComm(ulong overworldOffset, PokeTradeHubConfig config, CancellationToken token)
    {
        if (!await IsGameConnectedToYComm(token).ConfigureAwait(false))
        {
            Log("Reconectándose a Y-Comm...");
            await ReconnectToYComm(overworldOffset, config, token).ConfigureAwait(false);
        }
    }

    public async Task<byte> GetCurrentBox(CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync(CurrentBoxOffset, 1, token).ConfigureAwait(false);
        return data[0];
    }

    public async Task<uint> GetCurrentScreen(CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync(CurrentScreenOffset, 4, token).ConfigureAwait(false);
        return BitConverter.ToUInt32(data, 0);
    }

    /// <summary>
    /// Identifies the trainer information and loads the current runtime language.
    /// </summary>
    public async Task<SAV8SWSH> GetFakeTrainerSAV(CancellationToken token)
    {
        var sav = new SAV8SWSH();
        var info = sav.MyStatus;
        var read = await Connection.ReadBytesAsync(TrainerDataOffset, TrainerDataLength, token).ConfigureAwait(false);

        read.CopyTo(info.Data);
        return sav;
    }

    public async Task<TextSpeedOption> GetTextSpeed(CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync(TextSpeedOffset, 1, token).ConfigureAwait(false);
        return (TextSpeedOption)(data[0] & 3);
    }

    public async Task<SAV8SWSH> IdentifyTrainer(CancellationToken token)
    {
        // Check if botbase is on the correct version or later.
        await VerifyBotbaseVersion(token).ConfigureAwait(false);

        // Check title so we can warn if mode is incorrect.
        string title = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
        if (title is not (SwordID or ShieldID))
            throw new Exception($"{title} no es un título SWSH válido. ¿Tu modo es correcto?");

        // Verify the game version.
        var game_version = await SwitchConnection.GetGameInfo("version", token).ConfigureAwait(false);
        if (!game_version.SequenceEqual(SWSHGameVersion))
            throw new Exception($"La versión del juego no es compatible. Versión esperada {SWSHGameVersion} y la versión actual del juego es {game_version}.");

        Log("Tomando datos del entrenador de la consola host...");
        var sav = await GetFakeTrainerSAV(token).ConfigureAwait(false);
        InitSaveData(sav);

        if (!IsValidTrainerData())
        {
            await CheckForRAMShiftingApps(token).ConfigureAwait(false);
            throw new Exception("Consulte la wiki de SysBot.NET (https://github.com/kwsch/SysBot.NET/wiki/Troubleshooting) para obtener más información.");
        }

        if (await GetTextSpeed(token).ConfigureAwait(false) < TextSpeedOption.Fast)
            throw new Exception("La velocidad del texto debe configurarse en RÁPIDO. Solucione esto para un funcionamiento correcto.");

        return sav;
    }

    public async Task InitializeHardware(IBotStateSettings settings, CancellationToken token)
    {
        Log("Desconectando al inicio.");
        await DetachController(token).ConfigureAwait(false);
        if (settings.ScreenOff)
        {
            Log("Apagando la pantalla.");
            await SetScreen(ScreenState.Off, token).ConfigureAwait(false);
        }
    }

    public async Task<bool> IsCorrectScreen(uint expectedScreen, CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync(CurrentScreenOffset, 4, token).ConfigureAwait(false);
        return BitConverter.ToUInt32(data, 0) == expectedScreen;
    }

    public async Task<bool> IsGameConnectedToYComm(CancellationToken token)
    {
        // Reads the Y-Comm Flag to check if the game is connected online
        var data = await Connection.ReadBytesAsync(IsConnectedOffset, 1, token).ConfigureAwait(false);
        return data[0] == 1;
    }

    public async Task<bool> IsInBattle(CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync(Version == GameVersion.SH ? InBattleRaidOffsetSH : InBattleRaidOffsetSW, 1, token).ConfigureAwait(false);
        return data[0] == (Version == GameVersion.SH ? 0x40 : 0x41);
    }

    public async Task<bool> IsInBox(CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync(CurrentScreenOffset, 4, token).ConfigureAwait(false);
        var dataint = BitConverter.ToUInt32(data, 0);
        return dataint is CurrentScreen_Box1 or CurrentScreen_Box2;
    }

    public async Task<bool> IsOnOverworld(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 1;
    }

    public override Task<PK8> ReadBoxPokemon(int box, int slot, CancellationToken token)
    {
        var ofs = GetBoxSlotOffset(box, slot);
        return ReadPokemon(ofs, BoxFormatSlotSize, token);
    }

    public async Task<bool> ReadIsChanged(uint offset, byte[] original, CancellationToken token)
    {
        var result = await Connection.ReadBytesAsync(offset, original.Length, token).ConfigureAwait(false);
        return !result.SequenceEqual(original);
    }

    public override Task<PK8> ReadPokemon(ulong offset, CancellationToken token) => ReadPokemon(offset, BoxFormatSlotSize, token);

    public override async Task<PK8> ReadPokemon(ulong offset, int size, CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync((uint)offset, size, token).ConfigureAwait(false);
        return new PK8(data);
    }

    public override async Task<PK8> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(jumps, token).ConfigureAwait(false);
        if (!valid)
            return new PK8();
        return await ReadPokemon(offset, token).ConfigureAwait(false);
    }

    public async Task<PK8> ReadSurpriseTradePokemon(CancellationToken token)
    {
        var data = await Connection.ReadBytesAsync(SurpriseTradePartnerPokemonOffset, BoxFormatSlotSize, token).ConfigureAwait(false);
        return new PK8(data);
    }

    public async Task ReconnectToYComm(ulong overworldOffset, PokeTradeHubConfig config, CancellationToken token)
    {
        // Press B in case an Error Message is Present
        await Click(B, 2000, token).ConfigureAwait(false);

        // Return to Overworld
        if (!await IsOnOverworld(overworldOffset, token).ConfigureAwait(false))
        {
            for (int i = 0; i < 5; i++)
            {
                await Click(B, 500, token).ConfigureAwait(false);
            }
        }

        await Click(Y, 1000, token).ConfigureAwait(false);

        // Press it twice for safety -- sometimes misses it the first time.
        await Click(PLUS, 2_000, token).ConfigureAwait(false);
        await Click(PLUS, 5_000 + config.Timings.MiscellaneousSettings.ExtraTimeConnectOnline, token).ConfigureAwait(false);

        for (int i = 0; i < 5; i++)
        {
            await Click(B, 500, token).ConfigureAwait(false);
        }
    }

    public async Task ReOpenGame(PokeTradeHubConfig config, CancellationToken token)
    {
        // Reopen the game if we get soft-banned
        Log("Se detectó una posible prohibición suave, ¡reabriendo el juego por si acaso!");
        await CloseGame(config, token).ConfigureAwait(false);
        await StartGame(config, token).ConfigureAwait(false);

        // In case we are soft-banned, reset the timestamp
        await UnSoftBan(token).ConfigureAwait(false);
    }

    public Task SetBoxPokemon(PK8 pkm, int box, int slot, CancellationToken token, ITrainerInfo? sav = null)
    {
        if (sav != null)
        {
            pkm.UpdateHandler(sav);
            pkm.RefreshChecksum();
        }
        var ofs = GetBoxSlotOffset(box, slot);
        pkm.ResetPartyStats();
        return Connection.WriteBytesAsync(pkm.EncryptedPartyData, ofs, token);
    }

    public Task SetCurrentBox(byte box, CancellationToken token)
    {
        return Connection.WriteBytesAsync([box], CurrentBoxOffset, token);
    }

    public async Task SetTextSpeed(TextSpeedOption speed, CancellationToken token)
    {
        var textSpeedByte = await Connection.ReadBytesAsync(TextSpeedOffset, 1, token).ConfigureAwait(false);
        var data = new[] { (byte)((textSpeedByte[0] & 0xFC) | (int)speed) };
        await Connection.WriteBytesAsync(data, TextSpeedOffset, token).ConfigureAwait(false);
    }

    // Switches to box 1, then clears slot1 to prep fossil and egg bots.
    public async Task SetupBoxState(IDumper DumpSetting, CancellationToken token)
    {
        await SetCurrentBox(0, token).ConfigureAwait(false);

        var existing = await ReadBoxPokemon(0, 0, token).ConfigureAwait(false);
        if (existing.Species != 0 && existing.ChecksumValid)
        {
            Log("¡El espacio de destino está ocupado! Tirando los Pokémon encontrados allí...");
            DumpPokemon(DumpSetting.DumpFolder, "saved", existing);
        }

        Log("Limpiar la ranura de destino para iniciar el bot.");
        PK8 blank = new();
        await SetBoxPokemon(blank, 0, 0, token).ConfigureAwait(false);
    }

    public async Task StartGame(PokeTradeHubConfig config, CancellationToken token)
    {

        // Open game.
        var timing = config.Timings;
        var loadPro = timing.OpeningGameSettings.ProfileSelectionRequired ? timing.OpeningGameSettings.ExtraTimeLoadProfile : 0;

        await Click(A, 1_000 + loadPro, token).ConfigureAwait(false); // Initial "A" Press to start the Game + a delay if needed for profiles to load

        // Menus here can go in the order: Update Prompt -> Profile -> DLC check -> Unable to use DLC.
        //  The user can optionally turn on the setting if they know of a breaking system update incoming.
        if (timing.MiscellaneousSettings.AvoidSystemUpdate)
        {
            await Click(DUP, 0_600, token).ConfigureAwait(false);
            await Click(A, 1_000 + timing.OpeningGameSettings.ExtraTimeLoadProfile, token).ConfigureAwait(false);
        }

        // Only send extra Presses if we need to
        if (timing.OpeningGameSettings.ProfileSelectionRequired)
        {
            await Click(A, 1_000, token).ConfigureAwait(false); // Now we are on the Profile Screen
            await Click(A, 1_000, token).ConfigureAwait(false); // Select the profile
        }

        // Digital game copies take longer to load
        if (timing.OpeningGameSettings.CheckGameDelay)
        {
            await Task.Delay(2_000 + timing.OpeningGameSettings.ExtraTimeCheckGame, token).ConfigureAwait(false);
        }

        await Click(A, 0_600, token).ConfigureAwait(false);

        Log("¡Reiniciando el juego!");

        // Switch Logo lag, skip cutscene, game load screen
        await Task.Delay(10_000 + timing.OpeningGameSettings.ExtraTimeLoadGame, token).ConfigureAwait(false);

        for (int i = 0; i < 4; i++)
            await Click(A, 1_000, token).ConfigureAwait(false);

        var timer = 60_000;
        while (!await IsOnOverworldTitle(token).ConfigureAwait(false) && !await IsInBattle(token).ConfigureAwait(false))
        {
            await Task.Delay(0_200, token).ConfigureAwait(false);
            timer -= 0_250;

            // We haven't made it back to overworld after a minute, so press A every 6 seconds hoping to restart the game.
            // Don't risk it if hub is set to avoid updates.
            if (timer <= 0 && !timing.MiscellaneousSettings.AvoidSystemUpdate)
            {
                Log("¡Aún no estás en el juego, iniciando protocolo de rescate!");
                while (!await IsOnOverworldTitle(token).ConfigureAwait(false) && !await IsInBattle(token).ConfigureAwait(false))
                    await Click(A, 6_000, token).ConfigureAwait(false);
                break;
            }
        }

        Log("¡De vuelta en el Overworld!");
    }

    public Task UnSoftBan(CancellationToken token)
    {
        // Like previous generations, the game uses a Unix timestamp for
        // how long we are soft banned and once the soft ban is lifted
        // the game sets the value back to 0 (1970/01/01 12:00 AM (UTC))
        Log("Soft ban detectado, desbaneando");
        var data = BitConverter.GetBytes(0);
        return Connection.WriteBytesAsync(data, SoftBanUnixTimespanOffset, token);
    }

    protected virtual async Task EnterLinkCode(int code, PokeTradeHubConfig config, CancellationToken token)
    {
        // Default implementation to just press directional arrows. Can do via Hid keys, but users are slower than bots at even the default code entry.
        foreach (var key in TradeUtil.GetPresses(code))
        {
            int delay = config.Timings.MiscellaneousSettings.KeypressTime;
            await Click(key, delay, token).ConfigureAwait(false);
        }

        // Confirm Code outside of this method (allow synchronization)
    }

    private static uint GetBoxSlotOffset(int box, int slot) => BoxStartOffset + (uint)(BoxFormatSlotSize * ((30 * box) + slot));

    // Only used to check if we made it off the title screen.
    private async Task<bool> IsOnOverworldTitle(CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
        if (!valid)
            return false;
        return await IsOnOverworld(offset, token).ConfigureAwait(false);
    }
}
