
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
using System.Diagnostics;
using SysBot.Pokemon.Helpers;
using SysBot.Pokemon.WinForms.WebApi;

namespace SysBot.Pokemon.WinForms;

public static class WebApiExtensions
{
    private static BotServer? _server;
    private static TcpListener? _tcp;
    private static CancellationTokenSource? _cts;
    private static CancellationTokenSource? _monitorCts;
    private static Main? _main;
    private static System.Threading.Timer? _scheduleTimer;

    private const int WebPort = 8080;
    private static int _tcpPort = 0;
    private static readonly object _portLock = new object();
    private static readonly Dictionary<int, DateTime> _portReservations = new();

    public static void InitWebServer(this Main mainForm)
    {
        _main = mainForm;

        try
        {
            CleanupStalePortFiles();

            CheckPostRestartStartup(mainForm);

            if (IsPortInUse(WebPort))
            {
                lock (_portLock)
                {
                    _tcpPort = FindAvailablePort(8081);
                    ReservePort(_tcpPort);
                }
                StartTcpOnly();

                StartMasterMonitor();
                StartScheduledRestartTimer();
                return;
            }

            TryAddUrlReservation(WebPort);

            lock (_portLock)
            {
                _tcpPort = FindAvailablePort(8081);
                ReservePort(_tcpPort);
            }
            StartFullServer();

            StartScheduledRestartTimer();
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al inicializar el servidor web: {ex.Message}", "WebServer");
        }
    }

    private static void ReservePort(int port)
    {
        _portReservations[port] = DateTime.Now;
    }

    private static void ReleasePort(int port)
    {
        _portReservations.Remove(port);
    }

    private static void CleanupStalePortFiles()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;

            // Also clean up stale port reservations (older than 5 minutes)
            var staleReservations = _portReservations
                .Where(kvp => (DateTime.Now - kvp.Value).TotalMinutes > 5)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var port in staleReservations)
            {
                _portReservations.Remove(port);
            }

            var portFiles = Directory.GetFiles(exeDir, "DaiBot_*.port");

            foreach (var portFile in portFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(portFile);
                    var pidStr = fileName.Substring("DaiBot_".Length);

                    if (int.TryParse(pidStr, out int pid))
                    {
                        if (pid == Environment.ProcessId)
                            continue;

                        try
                        {
                            var process = Process.GetProcessById(pid);
                            if (process.ProcessName.Contains("SysBot", StringComparison.OrdinalIgnoreCase) ||
                                process.ProcessName.Contains("DaiBot", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (ArgumentException)
                        {
                        }

                        File.Delete(portFile);
                        LogUtil.LogInfo($"Se eliminó archivo de puerto obsoleto: {Path.GetFileName(portFile)}", "WebServer");
                    }
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Error al procesar el archivo de puerto {portFile}: {ex.Message}", "WebServer");
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al limpiar archivos de puerto obsoletos: {ex.Message}", "WebServer");
        }
    }

    private static void StartMasterMonitor()
    {
        _monitorCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var random = new Random();

            while (!_monitorCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(10000 + random.Next(5000), _monitorCts.Token);

                    if (UpdateManager.IsSystemUpdateInProgress || UpdateManager.IsSystemRestartInProgress)
                    {
                        continue;
                    }

                    if (!IsPortInUse(WebPort))
                    {
                        LogUtil.LogInfo("El servidor web maestro está caído. Intentando tomar control...", "WebServer");

                        await Task.Delay(random.Next(1000, 3000));

                        if (!IsPortInUse(WebPort) && !UpdateManager.IsSystemUpdateInProgress && !UpdateManager.IsSystemRestartInProgress)
                        {
                            TryTakeOverAsMaster();
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogUtil.LogError($"Error in master monitor: {ex.Message}", "WebServer");
                }
            }
        }, _monitorCts.Token);
    }

    private static void TryTakeOverAsMaster()
    {
        try
        {
            TryAddUrlReservation(WebPort);

            _server = new BotServer(_main!, WebPort, _tcpPort);
            _server.Start();

            _monitorCts?.Cancel();
            _monitorCts = null;

            LogUtil.LogInfo($"Se asumió correctamente como servidor web principal en el puerto {WebPort}", "WebServer");
            LogUtil.LogInfo($"La interfaz web ahora está disponible en http://localhost:{WebPort}", "WebServer");
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"No se pudo asumir como servidor principal: {ex.Message}", "WebServer");
            StartMasterMonitor();
        }
    }

    private static bool TryAddUrlReservation(int port)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"http add urlacl url=http://+:{port}/ user=Everyone",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas"
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit(5000);
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void StartTcpOnly()
    {
        StartTcp();
        CreatePortFile();
    }

    private static void StartFullServer()
    {
        try
        {
            _server = new BotServer(_main!, WebPort, _tcpPort);
            _server.Start();
            StartTcp();
            CreatePortFile();
        }
        catch (Exception ex) when (ex.Message.Contains("conflicts with an existing registration"))
        {
            // Otra instancia se convirtió en master primero - pasar a slave de forma elegante
            LogUtil.LogInfo("Conflicto en puerto 8080 durante inicio, iniciando como slave", "WebServer");
            StartTcpOnly();  // Esto creará el archivo de puerto como slave
        }
    }

    private static void StartTcp()
    {
        _cts = new CancellationTokenSource();
        var retryCount = 0;
        var maxRetries = 5;
        var random = new Random();

        Task.Run(async () =>
        {
            while (retryCount < maxRetries && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _tcp = new TcpListener(System.Net.IPAddress.Loopback, _tcpPort);
                    _tcp.Start();

                    LogUtil.LogInfo($"Escucha TCP iniciada correctamente en el puerto {_tcpPort}", "TCP");

                    while (!_cts.Token.IsCancellationRequested)
                    {
                        var tcpTask = _tcp.AcceptTcpClientAsync();
                        var tcs = new TaskCompletionSource<bool>();

                        using var registration = _cts.Token.Register(() => tcs.SetCanceled());
                        var completedTask = await Task.WhenAny(tcpTask, tcs.Task);
                        if (completedTask == tcpTask && tcpTask.IsCompletedSuccessfully)
                        {
                            _ = Task.Run(() => HandleClient(tcpTask.Result));
                        }
                    }
                    break; // Success, exit retry loop
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse && retryCount < maxRetries - 1)
                {
                    retryCount++;
                    LogUtil.LogInfo($"Puerto TCP {_tcpPort} en uso, buscando nuevo puerto (intento {retryCount}/{maxRetries})", "TCP");

                    // Wait a bit before retrying
                    await Task.Delay(random.Next(500, 1500));

                    // Find a new port
                    lock (_portLock)
                    {
                        ReleasePort(_tcpPort);
                        _tcpPort = FindAvailablePort(_tcpPort + 1);
                        ReservePort(_tcpPort);
                    }

                    // Update the port file with the new port
                    CreatePortFile();
                }
                catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
                {
                    LogUtil.LogError($"Error del listener TCP: {ex.Message}", "TCP");
                    throw;
                }
            }
            if (retryCount >= maxRetries)
            {
                LogUtil.LogError($"No se pudo iniciar el listener TCP después de {maxRetries} intentos", "TCP");
                throw new InvalidOperationException("No se pudo encontrar un puerto TCP disponible");
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
            "VERSION" => DaiBot.Version,
            "UPDATE" => TriggerUpdate(),
            "SELFRESTARTALL" => TriggerSelfRestart(),
            _ => $"ERROR: Unknown command '{cmd}'"
        };
    }

    private static string TriggerUpdate()
    {
        try
        {
            if (_main == null)
                return "ERROR: El formulario principal no está inicializado";

            _main.BeginInvoke((MethodInvoker)(async () =>
            {
                var(updateAvailable, _, newVersion) = await UpdateChecker.CheckForUpdatesAsync(false);
                if (updateAvailable)
                {
                    var updateForm = new UpdateForm(false, newVersion, true);
                    updateForm.PerformUpdate();
                }
            }));

            return "OK: Actualización iniciada";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
    }

    private static string TriggerSelfRestart()
    {
        try
        {
            if (_main == null)
                return "ERROR: El formulario principal no está inicializado";

            Task.Run(async () =>
            {
                await Task.Delay(2000);
                _main.BeginInvoke((MethodInvoker)(() =>
                {
                    Application.Restart();
                }));
            });

            return "OK: Reinicio iniciado";
        }
        catch (Exception ex)
        {
            return $"ERROR: {ex.Message}";
        }
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

            return $"OK: Comando {command} enviado a todos los bots";
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
                            Status = "Unknown",
                            ConnectionType = bot.Connection.Protocol.ToString(),
                            bot.Connection.IP,
                            bot.Connection.Port
                        });
                    }

                    return System.Text.Json.JsonSerializer.Serialize(new { Bots = botList });
                }
            }

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
            LogUtil.LogError($"GetBotsList error: {ex.Message}", "WebAPI");
            return $"ERROR: Failed to get bots list - {ex.Message}";
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

            return botController?.ReadBotState() ?? "ERROR: Bot not found";
        }
        catch (Exception ex)
        {
            return $"ERROR: Failed to get status - {ex.Message}";
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
            var mode = config?.Mode.ToString() ?? "Unknown";
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
            return $"ERROR: Failed to get instance info - {ex.Message}";
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
        return state.Connection.IP;
    }

    private static string GetVersion()
    {
        return DaiBot.Version;
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
            _ => "DaiBot"
        };
    }

    private static void CreatePortFile()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;
            var portFile = Path.Combine(exeDir, $"DaiBot_{Environment.ProcessId}.port");

            // Write with file lock to prevent race conditions
            using (var fs = new FileStream(portFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fs))
            {
                writer.WriteLine(_tcpPort);
            }
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
            var portFile = Path.Combine(exeDir, $"DaiBot_{Environment.ProcessId}.port");

            if (File.Exists(portFile))
                File.Delete(portFile);
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al eliminar el archivo de puerto: {ex.Message}", "WebServer");
        }
    }

    private static int FindAvailablePort(int startPort)
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var exeDir = Path.GetDirectoryName(exePath) ?? Program.WorkingDirectory;

        // Use a lock to prevent race conditions
        lock (_portLock)
        {
            for (int port = startPort; port < startPort + 100; port++)
            {
                // Check if port is reserved by another instance
                if (_portReservations.ContainsKey(port))
                    continue;

                if (!IsPortInUse(port))
                {
                    // Check if any port file claims this port
                    var portFiles = Directory.GetFiles(exeDir, "DaiBot_*.port");
                    bool portClaimed = false;

                    foreach (var file in portFiles)
                    {
                        try
                        {
                            // Lock the file before reading to prevent race conditions
                            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                            using (var reader = new StreamReader(fs))
                            {
                                var content = reader.ReadToEnd().Trim();
                                if (content == port.ToString() || content.Contains($"\"Port\":{port}"))
                                {
                                    portClaimed = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }

                    if (!portClaimed)
                    {
                        // Double-check the port is still available
                        if (!IsPortInUse(port))
                        {
                            return port;
                        }
                    }
                }
            }
        }
        throw new InvalidOperationException("No se encontraron puertos disponibles");
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMilliseconds(200) };
            var response = client.GetAsync($"http://localhost:{port}/api/bot/instances").Result;
            return response.IsSuccessStatusCode;
        }
        catch
        {
            try
            {
                using var tcpClient = new TcpClient();
                var result = tcpClient.BeginConnect("127.0.0.1", port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(200));
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
            _monitorCts?.Cancel();
            _cts?.Cancel();
            _tcp?.Stop();
            _server?.Dispose();
            _scheduleTimer?.Dispose();

            // Release the port reservation
            lock (_portLock)
            {
                ReleasePort(_tcpPort);
            }

            CleanupPortFile();
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al detener el servidor web: {ex.Message}", "WebServer");
        }
    }

    private static void CheckPostRestartStartup(Main mainForm)
    {
        try
        {
            var workingDir = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
            var restartFlagPath = Path.Combine(workingDir, "restart_in_progress.flag");
            var updateFlagPath = Path.Combine(workingDir, "update_in_progress.flag");

            bool isPostRestart = File.Exists(restartFlagPath);
            bool isPostUpdate = File.Exists(updateFlagPath);

            if (!isPostRestart && !isPostUpdate)
                return;

            string operation = isPostRestart ? "restart" : "update";
            LogUtil.LogInfo($"Inicio posterior a {operation} detectado. Esperando a que todas las instancias estén en línea...", operation == "reinicio" ? "RestartManager" : "UpdateManager");

            if (isPostRestart) File.Delete(restartFlagPath);
            if (isPostUpdate) File.Delete(updateFlagPath);

            Task.Run(async () =>
            {
                await Task.Delay(5000);

                var attempts = 0;
                var maxAttempts = 12;

                while (attempts < maxAttempts)
                {
                    try
                    {
                        LogUtil.LogInfo($"Intento {attempts + 1}/{maxAttempts} de verificación post-{operation}", operation == "restart" ? "RestartManager" : "UpdateManager");

                        mainForm.BeginInvoke((MethodInvoker)(() =>
                        {
                            var sendAllMethod = mainForm.GetType().GetMethod("SendAll",
                                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            sendAllMethod?.Invoke(mainForm, new object[] { BotControlCommand.Start });
                        }));

                        LogUtil.LogInfo("Comando Start All enviado a bots locales", operation == "restart" ? "RestartManager" : "UpdateManager");

                        var instances = GetAllRunningInstances(0);
                        if (instances.Count > 0)
                        {
                            LogUtil.LogInfo($"Encontradas {instances.Count} instancias remotas en línea. Enviando comando Start All...", operation == "restart" ? "RestartManager" : "UpdateManager");

                            // Enviar comandos de inicio en paralelo
                            var tasks = instances.Select(async instance =>
                            {
                                try
                                {
                                    await Task.Run(() =>
                                    {
                                        var response = BotServer.QueryRemote(instance.Port, "STARTALL");
                                        LogUtil.LogInfo($"Comando de inicio enviado al puerto {instance.Port}: {response}", operation == "restart" ? "RestartManager" : "UpdateManager");
                                    });
                                }
                                catch (Exception ex)
                                {
                                    LogUtil.LogError($"Error al enviar comando de inicio al puerto {instance.Port}: {ex.Message}", operation == "restart" ? "RestartManager" : "UpdateManager");
                                }
                            });

                            await Task.WhenAll(tasks);
                        }

                        LogUtil.LogInfo($"Comandos Start All post-{operation} completados con éxito", operation == "restart" ? "RestartManager" : "UpdateManager");
                        break;
                    }
                    catch (Exception ex)
                    {
                        LogUtil.LogError($"Error durante el intento {attempts + 1} de inicio post-{operation}: {ex.Message}", operation == "restart" ? "RestartManager" : "UpdateManager");
                    }

                    attempts++;
                    if (attempts < maxAttempts)
                        await Task.Delay(5000);
                }
            });
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al verificar el inicio después del reinicio/actualización: {ex.Message}", "StartupManager");
        }
    }

    private static List<(int Port, int ProcessId)> GetAllRunningInstances(int currentPort)
    {
        var instances = new List<(int, int)>();

        try
        {
            var processes = Process.GetProcessesByName("DaiBot")
                .Where(p => p.Id != Environment.ProcessId);

            foreach (var process in processes)
            {
                try
                {
                    var exePath = process.MainModule?.FileName;
                    if (string.IsNullOrEmpty(exePath))
                        continue;

                    var portFile = Path.Combine(Path.GetDirectoryName(exePath)!, $"DaiBot_{process.Id}.port");
                    if (!File.Exists(portFile))
                        continue;

                    var portText = File.ReadAllText(portFile).Trim();
                    if (!int.TryParse(portText, out var port))
                        continue;

                    if (IsPortInUse(port))
                    {
                        instances.Add((port, process.Id));
                    }
                }
                catch { }
            }
        }
        catch { }

        return instances;
    }

    private static void StartScheduledRestartTimer()
    {
        _scheduleTimer = new System.Threading.Timer(CheckScheduledRestart, null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private static void CheckScheduledRestart(object? state)
    {
        try
        {
            // Only master instance should check schedule
            if (_server == null) return;

            var workingDir = Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory;
            var schedulePath = Path.Combine(workingDir, "restart_schedule.json");
            if (!File.Exists(schedulePath))
                return;

            var scheduleJson = File.ReadAllText(schedulePath);
            var schedule = System.Text.Json.JsonSerializer.Deserialize<RestartSchedule>(scheduleJson);

            if (schedule == null || !schedule.Enabled)
                return;

            var now = DateTime.Now;
            var scheduledTime = DateTime.Parse(schedule.Time);
            scheduledTime = new DateTime(now.Year, now.Month, now.Day,
                scheduledTime.Hour, scheduledTime.Minute, 0);

            if (now.Hour == scheduledTime.Hour && now.Minute == scheduledTime.Minute)
            {
                var lastRestartPath = Path.Combine(workingDir, "last_restart.txt");
                if (File.Exists(lastRestartPath))
                {
                    var lastRestart = File.ReadAllText(lastRestartPath);
                    if (lastRestart == now.ToString("yyyy-MM-dd"))
                        return;
                }

                File.WriteAllText(lastRestartPath, now.ToString("yyyy-MM-dd"));

                LogUtil.LogInfo("Reinicio programado iniciado", "ScheduledRestart");

                if (_main != null)
                {
                    Task.Run(async () =>
                    {
                        await UpdateManager.RestartAllInstancesAsync(_main, _tcpPort);
                        await Task.Delay(10000);
                        await UpdateManager.ProceedWithRestartsAsync(_main, _tcpPort);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            LogUtil.LogError($"Error al verificar reinicio programado: {ex.Message}", "ScheduledRestart");
        }
    }

    private class RestartSchedule
    {
        public bool Enabled { get; set; }
        public string Time { get; set; } = "00:00";
    }
}
