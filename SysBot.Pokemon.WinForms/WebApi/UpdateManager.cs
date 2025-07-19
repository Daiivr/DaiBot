using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SysBot.Base;
using SysBot.Pokemon.Helpers;

namespace SysBot.Pokemon.WinForms.WebApi;

public static class UpdateManager
{
    public static bool IsSystemUpdateInProgress { get; private set; }
    public static bool IsSystemRestartInProgress { get; private set; }

    private static readonly Dictionary<string, UpdateStatus> _activeUpdates = [];

    public class UpdateAllResult
    {
        public int TotalInstances { get; set; }
        public int UpdatesNeeded { get; set; }
        public int UpdatesStarted { get; set; }
        public int UpdatesFailed { get; set; }
        public List<InstanceUpdateResult> InstanceResults { get; set; } = [];
    }

    public class InstanceUpdateResult
    {
        public int Port { get; set; }
        public int ProcessId { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public bool NeedsUpdate { get; set; }
        public bool UpdateStarted { get; set; }
        public string? Error { get; set; }
    }

    public class UpdateStatus
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime StartTime { get; set; } = DateTime.Now;
        public string Stage { get; set; } = "inicializando";
        public string Message { get; set; } = "Iniciando proceso de actualización...";
        public int Progress { get; set; } = 0;
        public bool IsComplete { get; set; }
        public bool Success { get; set; }
        public UpdateAllResult? Result { get; set; }
    }

    public static UpdateStatus? GetUpdateStatus(string updateId)
    {
        return _activeUpdates.TryGetValue(updateId, out var status) ? status : null;
    }

    public static List<UpdateStatus> GetActiveUpdates()
    {
        // Clean up old completed updates (older than 1 hour)
        var cutoffTime = DateTime.Now.AddHours(-1);
        var oldUpdates = _activeUpdates
            .Where(kvp => kvp.Value.IsComplete && kvp.Value.StartTime < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in oldUpdates)
        {
            _activeUpdates.Remove(key);
        }

        return [.. _activeUpdates.Values];
    }

    public static UpdateStatus StartBackgroundUpdate(Main mainForm, int currentPort)
    {
        var status = new UpdateStatus();
        _activeUpdates[status.Id] = status;

        // Start the ENTIRE update process in a fire-and-forget task
        _ = Task.Run(async () =>
        {
            try
            {
                LogUtil.LogInfo($"Iniciando proceso de actualización desatendido con ID: {status.Id}", "UpdateManager");

                // Phase 1: Check for updates
                status.Stage = "verificando";
                status.Message = "Buscando actualizaciones...";
                status.Progress = 5;

                var (updateAvailable, _, latestVersion) = await UpdateChecker.CheckForUpdatesAsync(false);
                if (!updateAvailable || string.IsNullOrEmpty(latestVersion))
                {
                    status.Stage = "completo";
                    status.Message = "No hay actualizaciones disponibles";
                    status.Progress = 100;
                    status.IsComplete = true;
                    status.Success = true;
                    LogUtil.LogInfo("No hay actualizaciones disponibles", "UpdateManager");
                    return;
                }

                LogUtil.LogInfo($"Actualización disponible: versión actual != {latestVersion}", "UpdateManager");

                // Phase 2: Identify instances needing updates
                status.Stage = "escaneando";
                status.Message = "Escaneando instancias...";
                status.Progress = 10;

                var instances = GetAllInstances(currentPort);
                LogUtil.LogInfo($"Se encontraron {instances.Count} instancias en total", "UpdateManager");

                var instancesNeedingUpdate = instances.Where(i => i.Version != latestVersion).ToList();
                LogUtil.LogInfo($"{instancesNeedingUpdate.Count} instancias necesitan actualización", "UpdateManager");

                if (instancesNeedingUpdate.Count == 0)
                {
                    status.Stage = "completo";
                    status.Message = "Todas las instancias ya están actualizadas";
                    status.Progress = 100;
                    status.IsComplete = true;
                    status.Success = true;
                    return;
                }

                status.Result = new UpdateAllResult
                {
                    TotalInstances = instances.Count,
                    UpdatesNeeded = instancesNeedingUpdate.Count
                };

                IsSystemUpdateInProgress = true;

                // Phase 3: Send idle command to all instances
                status.Stage = "inactivo";
                status.Message = $"Poniendo en inactividad todos los bots en {instancesNeedingUpdate.Count} instancias...";
                status.Progress = 20;

                await IdleAllInstances(mainForm, currentPort, instancesNeedingUpdate);
                // Minimal delay to ensure commands are received
                await Task.Delay(1000);

                // Phase 4: Wait for all bots to idle (with 3 minute timeout)
                status.Stage = "esperando_inactividad";
                status.Message = "Esperando a que todos los bots terminen sus operaciones actuales...";
                status.Progress = 30;

                var idleTimeout = DateTime.Now.AddMinutes(3);
                var allIdle = false;
                var lastIdleCheckTime = DateTime.Now;

                while (DateTime.Now < idleTimeout && !allIdle)
                {
                    allIdle = await CheckAllBotsIdleAsync(mainForm, currentPort);

                    if (!allIdle)
                    {
                        await Task.Delay(2000);
                        var elapsed = (DateTime.Now - status.StartTime).TotalSeconds;
                        var timeoutProgress = Math.Min(elapsed / 300 * 40, 40); // 300 seconds = 5 minutes
                        status.Progress = (int)(30 + timeoutProgress);

                        // Update message with time remaining
                        var remaining = (int)((300 - elapsed));
                        status.Message = $"Esperando a que todos los bots estén inactivos... (quedan {remaining}s)";

                        // Log every 10 seconds
                        if ((DateTime.Now - lastIdleCheckTime).TotalSeconds >= 10)
                        {
                            LogUtil.LogInfo($"Aún esperando a que los bots estén inactivos. Quedan {remaining}s", "UpdateManager");
                            lastIdleCheckTime = DateTime.Now;
                        }
                    }
                }

                if (!allIdle)
                {
                    LogUtil.LogInfo("Tiempo de espera agotado mientras se esperaba a que los bots estuvieran inactivos. FORZANDO actualización.", "UpdateManager");
                    status.Message = "Tiempo de espera agotado. FORZANDO actualización a pesar de bots activos...";
                    status.Progress = 65;

                    // Force stop all bots that aren't idle
                    await ForceStopAllBots(mainForm, currentPort, instancesNeedingUpdate);
                    await Task.Delay(1000);
                }
                else
                {
                    LogUtil.LogInfo("Todos los bots están inactivos. Procediendo con las actualizaciones.", "UpdateManager");
                }

                // Phase 5: Update all slave instances first (in parallel)
                status.Stage = "actualizando";
                status.Message = "Actualizando instancias esclavas...";
                status.Progress = 70;

                var slaveInstances = instancesNeedingUpdate.Where(i => i.ProcessId != Environment.ProcessId).ToList();
                var masterInstance = instancesNeedingUpdate.FirstOrDefault(i => i.ProcessId == Environment.ProcessId);

                LogUtil.LogInfo($"Actualizando {slaveInstances.Count} instancias esclavas en paralelo", "UpdateManager");

                // Update slaves sequentially with delay to avoid file conflicts
                var slaveResults = new List<InstanceUpdateResult>();

                for (int i = 0; i < slaveInstances.Count; i++)
                {
                    var slave = slaveInstances[i];
                    var instanceResult = new InstanceUpdateResult
                    {
                        Port = slave.Port,
                        ProcessId = slave.ProcessId,
                        CurrentVersion = slave.Version,
                        LatestVersion = latestVersion,
                        NeedsUpdate = true
                    };

                    try
                    {
                        LogUtil.LogInfo($"Iniciando actualización para la instancia en el puerto {slave.Port} ({i + 1}/{slaveInstances.Count})...", "UpdateManager");

                        // Update progress to show which slave is being updated
                        status.Message = $"Actualizando instancia esclava {i + 1} de {slaveInstances.Count} (Puerto: {slave.Port})";
                        status.Progress = 70 + (int)((i + 1) / (float)slaveInstances.Count * 20); // Progress from 70% to 90%

                        var updateResponse = BotServer.QueryRemote(slave.Port, "UPDATE");

                        if (!updateResponse.StartsWith("ERROR"))
                        {
                            instanceResult.UpdateStarted = true;
                            LogUtil.LogInfo($"Actualización iniciada para la instancia en el puerto {slave.Port}", "UpdateManager");

                            // Add delay between slave updates to avoid file conflicts
                            if (i < slaveInstances.Count - 1) // Don't delay after the last slave
                            {
                                LogUtil.LogInfo($"Esperando 3 segundos antes de la siguiente actualización para evitar conflictos de archivos...", "UpdateManager");
                                await Task.Delay(3000); // 3 second delay between slaves
                            }
                        }
                        else
                        {
                            instanceResult.Error = $"Error al iniciar la actualización: {updateResponse}";
                            LogUtil.LogError($"Error al iniciar la actualización para el puerto {slave.Port}: {updateResponse}", "UpdateManager");
                        }
                    }
                    catch (Exception ex)
                    {
                        instanceResult.Error = ex.Message;
                        LogUtil.LogError($"Error al actualizar la instancia en el puerto {slave.Port}: {ex.Message}", "UpdateManager");
                    }

                    slaveResults.Add(instanceResult);
                }

                status.Result.InstanceResults.AddRange(slaveResults);

                var successfulSlaves = slaveResults.Count(r => r.UpdateStarted);
                status.Result.UpdatesStarted = successfulSlaves;
                status.Result.UpdatesFailed = slaveResults.Count(r => !r.UpdateStarted);

                LogUtil.LogInfo($"Resultados de actualización de esclavos: {successfulSlaves} iniciadas, {status.Result.UpdatesFailed} fallidas", "UpdateManager");

                // Phase 6: Update master instance regardless of slave failures
                if (masterInstance.ProcessId != 0)
                {
                    status.Stage = "actualizando_maestro";
                    status.Message = "Actualizando instancia maestra...";
                    status.Progress = 90;

                    if (status.Result.UpdatesFailed > 0)
                    {
                        LogUtil.LogInfo($"Procediendo con la actualización maestra a pesar de {status.Result.UpdatesFailed} fallos en esclavos", "UpdateManager");
                    }

                    // Create flag file for post-update startup
                    var updateFlagPath = Path.Combine(
                        Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory,
                        "update_in_progress.flag"
                    );
                    File.WriteAllText(updateFlagPath, DateTime.Now.ToString());

                    var masterResult = new InstanceUpdateResult
                    {
                        Port = currentPort,
                        ProcessId = masterInstance.ProcessId,
                        CurrentVersion = masterInstance.Version,
                        LatestVersion = latestVersion,
                        NeedsUpdate = true
                    };

                    try
                    {
                        mainForm.BeginInvoke((MethodInvoker)(() =>
                        {
                            var updateForm = new UpdateForm(false, latestVersion, true);
                            updateForm.PerformUpdate();
                        }));

                        masterResult.UpdateStarted = true;
                        status.Result.UpdatesStarted++;
                        LogUtil.LogInfo("Actualización de la instancia maestra iniciada", "UpdateManager");
                    }
                    catch (Exception ex)
                    {
                        masterResult.Error = ex.Message;
                        status.Result.UpdatesFailed++;
                        LogUtil.LogError($"Error al actualizar la instancia maestra: {ex.Message}", "UpdateManager");
                    }

                    status.Result.InstanceResults.Add(masterResult);
                }
                else if (slaveInstances.Count > 0)
                {
                    // No master to update, wait a bit for slaves to restart then start all bots
                    LogUtil.LogInfo("No hay instancia maestra para actualizar. Esperando a que los esclavos reinicien...", "UpdateManager");
                    await Task.Delay(10000); // Give slaves time to restart

                    // Verify slaves came back online
                    var onlineCount = 0;
                    foreach (var slave in slaveInstances)
                    {
                        if (IsPortOpen(slave.Port))
                        {
                            onlineCount++;
                        }
                    }

                    LogUtil.LogInfo($"{onlineCount}/{slaveInstances.Count} esclavos volvieron a estar en línea", "UpdateManager");

                    await StartAllBots(mainForm, currentPort);
                }

                // Phase 7: Complete
                status.Stage = "completo";
                status.Success = status.Result.UpdatesStarted > 0; // Éxito si al menos una actualización inició
                status.Message = status.Success
                    ? $"Comandos de actualización enviados a {status.Result.UpdatesStarted} instancias. Ahora están actualizando..."
                    : $"Actualización fallida - ninguna instancia fue actualizada";
                status.Progress = 100;
                status.IsComplete = true;

                LogUtil.LogInfo($"Inicio de actualización completado: {status.Message}", "UpdateManager");
            }
            catch (Exception ex)
            {
                status.Stage = "error";
                status.Message = $"Actualización fallida: {ex.Message}";
                status.Progress = 0;
                status.IsComplete = true;
                status.Success = false;
                LogUtil.LogError($"Error en actualización desatendida: {ex}", "UpdateManager");
            }
            finally
            {
                IsSystemUpdateInProgress = false;
            }
        });

        return status;
    }

    private static Task IdleAllInstances(Main mainForm, int currentPort, List<(int ProcessId, int Port, string Version)> instances)
    {
        // Send idle commands in parallel
        var tasks = instances.Select(async instance =>
        {
            try
            {
                if (instance.ProcessId == Environment.ProcessId)
                {
                    // Idle local bots
                    mainForm.BeginInvoke((MethodInvoker)(() =>
                    {
                        var sendAllMethod = mainForm.GetType().GetMethod("SendAll",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        sendAllMethod?.Invoke(mainForm, [BotControlCommand.Idle]);
                    }));
                }
                else
                {
                    // Idle remote bots
                    await Task.Run(() =>
                    {
                        var idleResponse = BotServer.QueryRemote(instance.Port, "IDLEALL");
                        if (idleResponse.StartsWith("ERROR"))
                        {
                            LogUtil.LogError($"Error al enviar comando de inactividad al puerto {instance.Port}: {idleResponse}", "UpdateManager");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error al poner en inactividad la instancia en el puerto {instance.Port}: {ex.Message}", "UpdateManager");
            }
        });

        return Task.WhenAll(tasks);
    }

    private static Task ForceStopAllBots(Main mainForm, int currentPort, List<(int ProcessId, int Port, string Version)> instances)
    {
        LogUtil.LogInfo("Forzando detención de todos los bots debido a tiempo de espera inactividad", "UpdateManager");

        // Force stop in parallel
        var tasks = instances.Select(async instance =>
        {
            try
            {
                if (instance.ProcessId == Environment.ProcessId)
                {
                    // Stop local bots
                    mainForm.BeginInvoke((MethodInvoker)(() =>
                    {
                        var sendAllMethod = mainForm.GetType().GetMethod("SendAll",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        sendAllMethod?.Invoke(mainForm, [BotControlCommand.Stop]);
                    }));
                    LogUtil.LogInfo("Bots locales detenidos forzosamente", "UpdateManager");
                }
                else
                {
                    // Stop remote bots
                    await Task.Run(() =>
                    {
                        var stopResponse = BotServer.QueryRemote(instance.Port, "STOPALL");
                        if (!stopResponse.StartsWith("ERROR"))
                        {
                            LogUtil.LogInfo($"Bots detenidos forzosamente en el puerto {instance.Port}", "UpdateManager");
                        }
                        else
                        {
                            LogUtil.LogError($"Error al detener forzosamente los bots en el puerto {instance.Port}: {stopResponse}", "UpdateManager");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error al detener forzosamente los bots en el puerto {instance.Port}: {ex.Message}", "UpdateManager");
            }
        });

        return Task.WhenAll(tasks);
    }

    private static async Task StartAllBots(Main mainForm, int currentPort)
    {
        // Wait 10 seconds before starting all bots
        await Task.Delay(10000);
        LogUtil.LogInfo("Iniciando todos los bots después de la actualización...", "UpdateManager");

        // Start local bots
        mainForm.BeginInvoke((MethodInvoker)(() =>
        {
            var sendAllMethod = mainForm.GetType().GetMethod("SendAll",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            sendAllMethod?.Invoke(mainForm, [BotControlCommand.Start]);
        }));

        // Start remote bots in parallel
        var remoteInstances = GetAllInstances(currentPort).Where(i => i.ProcessId != Environment.ProcessId);
        var tasks = remoteInstances.Select(async instance =>
        {
            try
            {
                await Task.Run(() =>
                {
                    var response = BotServer.QueryRemote(instance.Port, "STARTALL");
                    LogUtil.LogInfo($"Comando de inicio enviado al puerto {instance.Port}: {response}", "UpdateManager");
                });
            }
            catch (Exception ex)
            {
                LogUtil.LogError($"Error al enviar comando de inicio al puerto {instance.Port}: {ex.Message}", "UpdateManager");
            }
        });

        await Task.WhenAll(tasks);
    }

    private static Task<bool> CheckAllBotsIdleAsync(Main mainForm, int currentPort)
    {
        try
        {
            // Check local bots
            var flpBotsField = mainForm.GetType().GetField("FLP_Bots",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (flpBotsField?.GetValue(mainForm) is FlowLayoutPanel flpBots)
            {
                var controllers = flpBots.Controls.OfType<BotController>().ToList();
                var anyActive = controllers.Any(c =>
                {
                    var state = c.ReadBotState();
                    return state != "IDLE" && state != "STOPPED";
                });
                if (anyActive) return Task.FromResult(false);
            }

            // Check remote instances
            var instances = GetAllInstances(currentPort);
            foreach (var (processId, port, version) in instances)
            {
                if (processId == Environment.ProcessId) continue;

                var botsResponse = BotServer.QueryRemote(port, "LISTBOTS");
                if (botsResponse.StartsWith("{") && botsResponse.Contains("Bots"))
                {
                    try
                    {
                        var botsData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, object>>>>(botsResponse);
                        if (botsData?.ContainsKey("Bots") == true)
                        {
                            var anyActive = botsData["Bots"].Any(b =>
                            {
                                if (b.TryGetValue("Status", out var status))
                                {
                                    var statusStr = status?.ToString()?.ToUpperInvariant() ?? "";
                                    return statusStr != "IDLE" && statusStr != "STOPPED";
                                }
                                return false;
                            });
                            if (anyActive) return Task.FromResult(false);
                        }
                    }
                    catch { }
                }
            }

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private static List<(int ProcessId, int Port, string Version)> GetAllInstances(int currentPort)
    {
        var instances = new List<(int, int, string)>
        {
            (Environment.ProcessId, currentPort, DaiBot.Version)
        };

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

                    if (!IsPortOpen(port))
                        continue;

                    var versionResponse = BotServer.QueryRemote(port, "VERSION");
                    var version = versionResponse.StartsWith("ERROR") ? "Unknown" : versionResponse.Trim();

                    instances.Add((process.Id, port, version));
                }
                catch { }
            }
        }
        catch { }

        return instances;
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var result = client.BeginConnect("127.0.0.1", port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
            if (success)
            {
                client.EndConnect(result);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // Restart functionality
    public class RestartAllResult
    {
        public int TotalInstances { get; set; }
        public List<RestartInstanceResult> InstanceResults { get; set; } = [];
        public bool Success { get; set; }
        public string? Error { get; set; }
        public bool MasterRestarting { get; set; }
        public string? Message { get; set; }
    }

    public class RestartInstanceResult
    {
        public int Port { get; set; }
        public int ProcessId { get; set; }
        public bool RestartStarted { get; set; }
        public string? Error { get; set; }
    }

    public static async Task<RestartAllResult> RestartAllInstancesAsync(Main mainForm, int currentPort)
    {
        var result = new RestartAllResult();

        // Check if restart already in progress
        var lockFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "restart.lock");
        try
        {
            // Try to create lock file exclusively
            using var fs = new FileStream(lockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var writer = new StreamWriter(fs);
            writer.WriteLine($"{Environment.ProcessId}:{DateTime.Now}");
            writer.Flush();
        }
        catch (IOException)
        {
            // Lock file exists, restart already in progress
            result.Success = false;
            result.Error = "Reinicio ya está en curso por otra instancia";
            return result;
        }

        IsSystemRestartInProgress = true;

        try
        {
            var instances = GetAllInstances(currentPort);
            result.TotalInstances = instances.Count;

            LogUtil.LogInfo($"Preparando para reiniciar {instances.Count} instancias", "RestartManager");
            LogUtil.LogInfo("Poniendo en inactividad todos los bots antes del reinicio...", "RestartManager");

            // Send idle commands to all instances
            await IdleAllInstances(mainForm, currentPort, instances);
            await Task.Delay(1000); // Give commands time to process

            // Wait for all bots to actually be idle (with timeout)
            LogUtil.LogInfo("Esperando a que todos los bots estén inactivos...", "RestartManager");
            var idleTimeout = DateTime.Now.AddMinutes(3);
            var allIdle = false;

            while (DateTime.Now < idleTimeout && !allIdle)
            {
                allIdle = await CheckAllBotsIdleAsync(mainForm, currentPort);

                if (!allIdle)
                {
                    await Task.Delay(2000);
                    var timeRemaining = (int)(idleTimeout - DateTime.Now).TotalSeconds;
                    LogUtil.LogInfo($"Aún esperando a que los bots estén inactivos... quedan {timeRemaining}s", "RestartManager");
                }
            }

            if (!allIdle)
            {
                LogUtil.LogInfo("Tiempo de espera agotado mientras se esperaba a los bots. FORZANDO detención de todos los bots...", "RestartManager");
                await ForceStopAllBots(mainForm, currentPort, instances);
                await Task.Delay(2000); // Give stop commands time to process
            }
            else
            {
                LogUtil.LogInfo("Todos los bots están inactivos. Listos para proceder con el reinicio.", "RestartManager");
            }

            result.Success = true;
            result.Message = allIdle ? "Todos los bots inactivos correctamente" : "Detención forzada tras tiempo de espera";
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
        finally
        {
            IsSystemRestartInProgress = false;
        }
    }

    public static async Task<RestartAllResult> ProceedWithRestartsAsync(Main mainForm, int currentPort)
    {
        var result = new RestartAllResult();
        IsSystemRestartInProgress = true;

        try
        {
            var instances = GetAllInstances(currentPort);
            result.TotalInstances = instances.Count;

            var slaveInstances = instances.Where(i => i.ProcessId != Environment.ProcessId).ToList();
            var masterInstance = instances.FirstOrDefault(i => i.ProcessId == Environment.ProcessId);

            // Restart slaves one by one
            foreach (var instance in slaveInstances)
            {
                var instanceResult = new RestartInstanceResult
                {
                    Port = instance.Port,
                    ProcessId = instance.ProcessId
                };

                try
                {
                    LogUtil.LogInfo($"Enviando comando de reinicio a la instancia en el puerto {instance.Port}...", "RestartManager");

                    var restartResponse = BotServer.QueryRemote(instance.Port, "SELFRESTARTALL");
                    if (!restartResponse.StartsWith("ERROR"))
                    {
                        instanceResult.RestartStarted = true;
                        LogUtil.LogInfo($"Comando de reinicio enviado a la instancia en el puerto {instance.Port}, esperando terminación...", "RestartManager");

                        // Esperar a que el proceso realmente termine
                        var terminated = await WaitForProcessTermination(instance.ProcessId, 30);
                        if (!terminated)
                        {
                            LogUtil.LogError($"La instancia {instance.ProcessId} no terminó a tiempo", "RestartManager");
                        }
                        else
                        {
                            LogUtil.LogInfo($"La instancia {instance.ProcessId} terminó correctamente", "RestartManager");
                        }

                        // Wait a bit for cleanup
                        await Task.Delay(2000);

                        // Wait for instance to come back online
                        var backOnline = await WaitForInstanceOnline(instance.Port, 60);
                        if (backOnline)
                        {
                            LogUtil.LogInfo($"La instancia en el puerto {instance.Port} está de nuevo en línea", "RestartManager");
                        }
                    }
                    else
                    {
                        instanceResult.Error = $"Error al enviar comando de reinicio: {restartResponse}";
                        LogUtil.LogError($"Error al reiniciar la instancia en el puerto {instance.Port}: {restartResponse}", "RestartManager");
                    }
                }
                catch (Exception ex)
                {
                    instanceResult.Error = ex.Message;
                    LogUtil.LogError($"Error al reiniciar la instancia en el puerto {instance.Port}: {ex.Message}", "RestartManager");
                }

                result.InstanceResults.Add(instanceResult);
            }

            if (masterInstance.ProcessId != 0)
            {
                LogUtil.LogInfo("Preparando para reiniciar la instancia maestra...", "RestartManager");
                result.MasterRestarting = true;

                var restartFlagPath = Path.Combine(
                    Path.GetDirectoryName(Application.ExecutablePath) ?? Environment.CurrentDirectory,
                    "restart_in_progress.flag"
                );
                File.WriteAllText(restartFlagPath, DateTime.Now.ToString());

                await Task.Delay(2000);

                mainForm.BeginInvoke((MethodInvoker)(() =>
                {
                    Application.Restart();
                }));
            }

            result.Success = true;
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }
        finally
        {
            IsSystemRestartInProgress = false;

            // Clean up lock file
            var lockFile = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "restart.lock");
            try
            {
                if (File.Exists(lockFile))
                    File.Delete(lockFile);
            }
            catch { }
        }
    }

    private static async Task<bool> WaitForProcessTermination(int processId, int timeoutSeconds)
    {
        var endTime = DateTime.Now.AddSeconds(timeoutSeconds);

        while (DateTime.Now < endTime)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    return true;
            }
            catch (ArgumentException)
            {
                // Process not found = terminated
                return true;
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task<bool> WaitForInstanceOnline(int port, int timeoutSeconds)
    {
        var endTime = DateTime.Now.AddSeconds(timeoutSeconds);

        while (DateTime.Now < endTime)
        {
            if (IsPortOpen(port))
            {
                // Give it a moment to fully initialize
                await Task.Delay(1000);
                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }
}
