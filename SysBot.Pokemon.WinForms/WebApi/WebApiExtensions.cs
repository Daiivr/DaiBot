using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SysBot.Base;
using SysBot.Pokemon.WinForms.WebApi;

namespace SysBot.Pokemon.WinForms;

public static class WebApiExtensions
{
    private static BotServer? _server;
    private static TcpListener? _tcp;
    private static CancellationTokenSource? _cts;
    private static Main? _main;

    private const int WebPort = 8080;
    private static int _tcpPort = 0;

    public static void InitWebServer(this Main mainForm)
    {
        _main = mainForm;

        try
        {
            if (IsPortInUse(WebPort))
            {
                _tcpPort = FindAvailablePort(8081);
                StartTcpOnly();
                return;
            }

            _tcpPort = FindAvailablePort(8081);
            StartFullServer();
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al inicializar el servidor web: {ex.Message}", "WebServer");
        }
    }

    private static void StartTcpOnly()
    {
        CreatePortFile();
        StartTcp();
    }

    private static void StartFullServer()
    {
        CreatePortFile();
        _server = new BotServer(_main!, WebPort, _tcpPort);
        _server.Start();
        StartTcp();
    }

    private static void StartTcp()
    {
        _cts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            try
            {
                _tcp = new TcpListener(System.Net.IPAddress.Loopback, _tcpPort);
                _tcp.Start();

                while (!_cts.Token.IsCancellationRequested)
                {
                    var tcpTask = _tcp.AcceptTcpClientAsync();
                    var tcs = new TaskCompletionSource<bool>();

                    using (var registration = _cts.Token.Register(() => tcs.SetCanceled()))
                    {
                        var completedTask = await Task.WhenAny(tcpTask, tcs.Task);
                        if (completedTask == tcpTask && tcpTask.IsCompletedSuccessfully)
                        {
                            _ = Task.Run(() => HandleClient(tcpTask.Result));
                        }
                    }
                }
            }
            catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
            {
                LogUtil.LogError($"Error en el listener TCP: {ex.Message}", "TCP");
            }
        });
    }

    private static async Task HandleClient(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;

                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                var command = await reader.ReadLineAsync();
                if (!string.IsNullOrEmpty(command))
                {
                    var response = ProcessCommand(command);
                    await writer.WriteLineAsync(response);
                    await stream.FlushAsync();
                    await Task.Delay(100);
                }
            }
        }
        catch (Exception ex) when (!(ex is IOException { InnerException: SocketException }))
        {
            LogUtil.LogError($"Error al manejar el cliente TCP: {ex.Message}", "TCP");
        }
    }

    private static string ProcessCommand(string command)
    {
        if (_main == null)
            return "ERROR: Formulario principal no inicializado";

        var parts = command.Split(':');
        var cmd = parts[0].ToUpperInvariant();
        var botId = parts.Length > 1 ? parts[1] : null;

        return cmd switch
        {
            "STARTALL" => ExecuteGlobalCommand(BotControlCommand.Start),
            "STOPALL" => ExecuteGlobalCommand(BotControlCommand.Stop),
            "IDLEALL" => ExecuteGlobalCommand(BotControlCommand.Idle),
            "RESUMEALL" => ExecuteGlobalCommand(BotControlCommand.Resume),
            "RESTARTALL" => ExecuteGlobalCommand(BotControlCommand.Restart),
            "REBOOTALL" => ExecuteGlobalCommand(BotControlCommand.RebootAndStop),
            "SCREENONALL" => ExecuteGlobalCommand(BotControlCommand.ScreenOnAll),
            "SCREENOFFALL" => ExecuteGlobalCommand(BotControlCommand.ScreenOffAll),
            "LISTBOTS" => GetBotsList(),
            "STATUS" => GetBotStatuses(botId),
            "ISREADY" => CheckReady(),
            "INFO" => GetInstanceInfo(),
            _ => $"ERROR: Comando desconocido '{cmd}'"
        };
    }

    private static string ExecuteGlobalCommand(BotControlCommand command)
    {
        try
        {
            _main!.BeginInvoke((MethodInvoker)(() =>
            {
                var sendAllMethod = _main.GetType().GetMethod("SendAll",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                sendAllMethod?.Invoke(_main, new object[] { command });
            }));

            return $"OK: comando {command} enviado a todos los bots";
        }
        catch (Exception ex)
        {
            return $"ERROR: Falló la ejecución de {command} - {ex.Message}";
        }
    }

    private static string GetBotsList()
    {
        try
        {
            var botList = new List<object>();
            var config = GetConfig();
            var controllers = GetBotControllers();

            // If no controllers found in UI, try to get from Bots property
            if (controllers.Count == 0)
            {
                var botsProperty = _main!.GetType().GetProperty("Bots",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (botsProperty?.GetValue(_main) is List<PokeBotState> bots)
                {
                    foreach (var bot in bots)
                    {
                        botList.Add(new
                        {
                            Id = $"{bot.Connection.IP}:{bot.Connection.Port}",
                            Name = bot.Connection.IP,
                            RoutineType = bot.InitialRoutine.ToString(),
                            Status = "Desconocido",
                            ConnectionType = bot.Connection.Protocol.ToString(),
                            bot.Connection.IP,
                            bot.Connection.Port
                        });
                    }

                    return System.Text.Json.JsonSerializer.Serialize(new { Bots = botList });
                }
            }

            // Use controllers if available
            foreach (var controller in controllers)
            {
                var state = controller.State;
                var botName = GetBotName(state, config);
                var status = controller.ReadBotState();

                botList.Add(new
                {
                    Id = $"{state.Connection.IP}:{state.Connection.Port}",
                    Name = botName,
                    RoutineType = state.InitialRoutine.ToString(),
                    Status = status,
                    ConnectionType = state.Connection.Protocol.ToString(),
                    state.Connection.IP,
                    state.Connection.Port
                });
            }

            return System.Text.Json.JsonSerializer.Serialize(new { Bots = botList });
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error en GetBotsList: {ex.Message}", "WebAPI");
            return $"ERROR: Falló obtener la lista de bots - {ex.Message}";
        }
    }

    private static string GetBotStatuses(string? botId)
    {
        try
        {
            var config = GetConfig();
            var controllers = GetBotControllers();

            if (string.IsNullOrEmpty(botId))
            {
                var statuses = controllers.Select(c => new
                {
                    Id = $"{c.State.Connection.IP}:{c.State.Connection.Port}",
                    Name = GetBotName(c.State, config),
                    Status = c.ReadBotState()
                }).ToList();

                return System.Text.Json.JsonSerializer.Serialize(statuses);
            }

            var botController = controllers.FirstOrDefault(c =>
                $"{c.State.Connection.IP}:{c.State.Connection.Port}" == botId);

            return botController?.ReadBotState() ?? "ERROR: Bot no encontrado";
        }
        catch (Exception ex)
        {
            return $"ERROR: Falló obtener el estado - {ex.Message}";
        }
    }

    private static string CheckReady()
    {
        try
        {
            var controllers = GetBotControllers();
            var hasRunningBots = controllers.Any(c => c.GetBot()?.IsRunning ?? false);
            return hasRunningBots ? "READY" : "NOT_READY";
        }
        catch
        {
            return "NOT_READY";
        }
    }

    private static string GetInstanceInfo()
    {
        try
        {
            var config = GetConfig();
            var version = GetVersion();
            var mode = config?.Mode.ToString() ?? "Desconocido";
            var name = GetInstanceName(config, mode);

            var info = new
            {
                Version = version,
                Mode = mode,
                Name = name,
                Environment.ProcessId,
                Port = _tcpPort
            };

            return System.Text.Json.JsonSerializer.Serialize(info);
        }
        catch (Exception ex)
        {
            return $"ERROR: Falló obtener la información de la instancia - {ex.Message}";
        }
    }

    private static List<BotController> GetBotControllers()
    {
        var flpBotsField = _main!.GetType().GetField("FLP_Bots",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (flpBotsField?.GetValue(_main) is FlowLayoutPanel flpBots)
        {
            return [.. flpBots.Controls.OfType<BotController>()];
        }

        return [];
    }

    private static ProgramConfig? GetConfig()
    {
        var configProp = _main?.GetType().GetProperty("Config",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return configProp?.GetValue(_main) as ProgramConfig;
    }

    private static string GetBotName(PokeBotState state, ProgramConfig? config)
    {
        // Always return IP address as the bot name
        return state.Connection.IP;
    }

    private static string GetVersion()
    {
        try
        {
            // Get the TradeBot type from the Helpers namespace
            var tradeBotType = Type.GetType("SysBot.Pokemon.Helpers.PokeBot, SysBot.Pokemon");
            if (tradeBotType != null)
            {
                var versionField = tradeBotType.GetField("Version",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (versionField != null)
                {
                    return versionField.GetValue(null)?.ToString() ?? "Desconocido";
                }
            }

            // Fallback to assembly version
            return _main!.GetType().Assembly.GetName().Version?.ToString() ?? "Desconocido";
        }
        catch
        {
            return "Desconocido";
        }
    }

    private static string GetInstanceName(ProgramConfig? config, string mode)
    {
        if (!string.IsNullOrEmpty(config?.Hub?.BotName))
            return config.Hub.BotName;

        return mode switch
        {
            "LGPE" => "LGPE",
            "BDSP" => "BDSP",
            "SWSH" => "SWSH",
            "SV" => "SV",
            "LA" => "LA",
            _ => "PokeBot"
        };
    }

    private static void CreatePortFile()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;
            var portFile = Path.Combine(exeDir, $"PokeBot_{Environment.ProcessId}.port");
            File.WriteAllText(portFile, _tcpPort.ToString());
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al crear el archivo de puerto: {ex.Message}", "WebServer");
        }
    }

    private static void CleanupPortFile()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;
            var portFile = Path.Combine(exeDir, $"PokeBot_{Environment.ProcessId}.port");

            if (File.Exists(portFile))
                File.Delete(portFile);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al limpiar el archivo de puerto: {ex.Message}", "WebServer");
        }
    }

    private static int FindAvailablePort(int startPort)
    {
        for (int port = startPort; port < startPort + 100; port++)
        {
            if (!IsPortInUse(port))
                return port;
        }
        throw new InvalidOperationException("No se encontraron puertos disponibles");
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = client.GetAsync($"http://localhost:{port}/api/bot/instances").Result;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            // Also check TCP
            try
            {
                using var tcpClient = new TcpClient();
                var result = tcpClient.BeginConnect("127.0.0.1", port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                if (success)
                {
                    tcpClient.EndConnect(result);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    public static void StopWebServer(this Main mainForm)
    {
        try
        {
            _cts?.Cancel();
            _tcp?.Stop();
            _server?.Dispose();
            CleanupPortFile();
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al detener el servidor web: {ex.Message}", "WebServer");
        }
    }
}
