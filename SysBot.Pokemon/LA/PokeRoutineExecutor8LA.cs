using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.PokeDataOffsetsLA;

namespace SysBot.Pokemon;

public abstract class PokeRoutineExecutor8LA : PokeRoutineExecutor<PA8>
{
    protected PokeRoutineExecutor8LA(PokeBotState Config) : base(Config)
    {
    }

    protected PokeDataOffsetsLA Offsets { get; } = new();

    public async Task<bool> CheckIfSoftBanned(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 4, token).ConfigureAwait(false);
        return BitConverter.ToUInt32(data, 0) != 0;
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

    public async Task<byte> GetCurrentBox(CancellationToken token)
    {
        var data = await SwitchConnection.PointerPeek(1, Offsets.CurrentBoxPointer, token).ConfigureAwait(false);
        return data[0];
    }

    public async Task<SAV8LA> GetFakeTrainerSAV(CancellationToken token)
    {
        var sav = new SAV8LA();
        var info = sav.MyStatus;
        var read = await SwitchConnection.PointerPeek(info.Data.Length, Offsets.MyStatusPointer, token).ConfigureAwait(false);
        read.ToArray().CopyTo(info.Data);
        return sav;
    }

    public async Task<TextSpeedOption> GetTextSpeed(CancellationToken token)
    {
        var data = await SwitchConnection.PointerPeek(1, Offsets.TextSpeedPointer, token).ConfigureAwait(false);
        return (TextSpeedOption)data[0];
    }

    public async Task<ulong> GetTradePartnerNID(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 8, token).ConfigureAwait(false);
        return BitConverter.ToUInt64(data, 0);
    }

    public async Task<SAV8LA> IdentifyTrainer(CancellationToken token)
    {
        // Check if botbase is on the correct version or later.
        await VerifyBotbaseVersion(token).ConfigureAwait(false);

        // Check title so we can warn if mode is incorrect.
        string title = await SwitchConnection.GetTitleID(token).ConfigureAwait(false);
        if (title != LegendsArceusID)
            throw new Exception($"{title} no es un título válido de Pokémon Legends: Arceus. ¿Tu modo es correcto?");

        // Verify the game version.
        var game_version = await SwitchConnection.GetGameInfo("version", token).ConfigureAwait(false);
        if (!game_version.SequenceEqual(LAGameVersion))
            throw new Exception($"La versión del juego no es compatible. Versión esperada {LAGameVersion} y la versión actual del juego es {game_version}.");

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

    public async Task<bool> IsOnOverworld(ulong offset, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 1, token).ConfigureAwait(false);
        return data[0] == 1;
    }

    public override Task<PA8> ReadBoxPokemon(int box, int slot, CancellationToken token)
    {
        // Shouldn't be reading anything but box1slot1 here. Slots are not consecutive.
        var jumps = Offsets.BoxStartPokemonPointer.ToArray();
        return ReadPokemonPointer(jumps, BoxFormatSlotSize, token);
    }

    public async Task<bool> ReadIsChanged(uint offset, byte[] original, CancellationToken token)
    {
        var result = await Connection.ReadBytesAsync(offset, original.Length, token).ConfigureAwait(false);
        return !result.SequenceEqual(original);
    }

    public override Task<PA8> ReadPokemon(ulong offset, CancellationToken token) => ReadPokemon(offset, BoxFormatSlotSize, token);

    public override async Task<PA8> ReadPokemon(ulong offset, int size, CancellationToken token)
    {
        var data = await SwitchConnection.ReadBytesAbsoluteAsync(offset, size, token).ConfigureAwait(false);
        return new PA8(data);
    }

    public override async Task<PA8> ReadPokemonPointer(IEnumerable<long> jumps, int size, CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(jumps, token).ConfigureAwait(false);
        if (!valid)
            return new PA8();
        return await ReadPokemon(offset, token).ConfigureAwait(false);
    }

    public async Task ReOpenGame(PokeTradeHubConfig config, CancellationToken token)
    {
        Log("Error detectado, reiniciando el juego!!");
        await CloseGame(config, token).ConfigureAwait(false);
        await StartGame(config, token).ConfigureAwait(false);
    }

    public Task SetBoxPokemonAbsolute(ulong offset, PA8 pkm, CancellationToken token, ITrainerInfo? sav = null)
    {
        if (sav != null)
        {
            // Update PKM to the current save's handler data
            pkm.UpdateHandler(sav);
            pkm.RefreshChecksum();
        }

        pkm.ResetPartyStats();
        return SwitchConnection.WriteBytesAbsoluteAsync(pkm.EncryptedBoxData, offset, token);
    }

    public Task SetCurrentBox(byte box, CancellationToken token)
    {
        return SwitchConnection.PointerPoke([box], Offsets.CurrentBoxPointer, token);
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

        Log("¡Reiniciando el juego!");

        // Switch Logo and game load screen
        await Task.Delay(12_000 + timing.OpeningGameSettings.ExtraTimeLoadGame, token).ConfigureAwait(false);

        for (int i = 0; i < 8; i++)
            await Click(A, 1_000, token).ConfigureAwait(false);

        var timer = 60_000;
        while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
        {
            await Task.Delay(1_000, token).ConfigureAwait(false);
            timer -= 1_000;

            // We haven't made it back to overworld after a minute, so press A every 6 seconds hoping to restart the game.
            // Don't risk it if hub is set to avoid updates.
            if (timer <= 0 && !timing.MiscellaneousSettings.AvoidSystemUpdate)
            {
                Log("¡Aún no estás en el juego, iniciando protocolo de rescate!");
                while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
                    await Click(A, 6_000, token).ConfigureAwait(false);
                break;
            }
        }

        await Task.Delay(timing.OpeningGameSettings.ExtraTimeLoadOverworld, token).ConfigureAwait(false);
        Log("¡De vuelta en el Overworld!");
    }

    public Task UnSoftBan(CancellationToken token)
    {
        Log("Soft ban detectado, desbloqueando");

        // Write the value to 0.
        var data = BitConverter.GetBytes(0);
        return SwitchConnection.PointerPoke(data, Offsets.SoftbanPointer, token);
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

    // Only used to check if we made it off the title screen; the pointer isn't viable until a few seconds after clicking A.
    private async Task<bool> IsOnOverworldTitle(CancellationToken token)
    {
        var (valid, offset) = await ValidatePointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
        if (!valid)
            return false;
        return await IsOnOverworld(offset, token).ConfigureAwait(false);
    }
}
