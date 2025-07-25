using PKHeX.Core;
using SysBot.Base;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

public class QueueMonitor<T>(PokeTradeHub<T> Hub)
    where T : PKM, new()
{

    public async Task MonitorOpenQueue(CancellationToken token)
    {
        var queues = Hub.Queues.Info;
        var settings = Hub.Config.Queues;
        float secWaited = 0;

        const int sleepDelay = 0_500;
        const float sleepSeconds = sleepDelay / 1000f;
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(sleepDelay, token).ConfigureAwait(false);
            var mode = settings.QueueToggleMode;
            if (!UpdateCanQueue(mode, settings, queues, secWaited))
            {
                secWaited += sleepSeconds;
                continue;
            }

            // Queue setting has been updated. Echo out that things have changed.
            secWaited = 0;
            var state = queues.GetCanQueue()
                ? $"✅ Los usuarios ahora pueden unirse a la cola comercial."
                : $"⚠️ Configuración de la cola modificada: **Los usuarios NO PUEDEN unirse a la cola hasta que se vuelva a activar.**";
            EchoUtil.Echo(state);
        }
    }

    private static bool CheckInterval(QueueSettings settings, TradeQueueInfo<T> queues, float secWaited)
    {
        if (settings.CanQueue)
        {
            if (secWaited >= settings.IntervalOpenFor)
                queues.ToggleQueue();
            else
                return false;
        }
        else
        {
            if (secWaited >= settings.IntervalCloseFor)
                queues.ToggleQueue();
            else
                return false;
        }

        return true;
    }

    private static bool CheckThreshold(QueueSettings settings, TradeQueueInfo<T> queues)
    {
        if (settings.CanQueue)
        {
            if (queues.Count >= settings.ThresholdLock)
                queues.ToggleQueue();
            else
                return false;
        }
        else
        {
            if (queues.Count <= settings.ThresholdUnlock)
                queues.ToggleQueue();
            else
                return false;
        }

        return true;
    }

    private static bool UpdateCanQueue(QueueOpening mode, QueueSettings settings, TradeQueueInfo<T> queues, float secWaited)
    {
        return mode switch
        {
            QueueOpening.Threshold => CheckThreshold(settings, queues),
            QueueOpening.Interval => CheckInterval(settings, queues, secWaited),
            _ => false,
        };
    }
}
