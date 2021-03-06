using System;
using System.Threading;
using System.Threading.Tasks;
using Hubbup.Web.DataSources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hubbup.Web
{
    public class DataLoadingService : IHostedService
    {
        private static readonly TimeSpan TimerPeriod = TimeSpan.FromMinutes(10);
        //private static readonly TimeSpan TimerPeriod = TimeSpan.FromSeconds(15);

        private readonly IDataSource _dataSource;
        private readonly ILogger _logger;

        private readonly Timer _timer;
        private int _loading;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public DataLoadingService(IDataSource dataSource, ILogger<DataLoadingService> logger)
        {
            _dataSource = dataSource;
            _logger = logger;

            // Set up the timer in constructor, but don't actually start it until Start
            _timer = new Timer(state => ((DataLoadingService)state).OnTimerAsync(), this, Timeout.Infinite, Timeout.Infinite);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Start the reload timer
            _timer.Change(TimerPeriod, TimerPeriod);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();
            _timer.Dispose();
            return Task.CompletedTask;
        }

        // ASYNC VOID! It makes sense here. The timer will keep firing. That's also why we have the `_loading` value.
        private async void OnTimerAsync()
        {
            if (Interlocked.CompareExchange(ref _loading, 1, 0) == 0)
            {
                _logger.LogTrace("Reloading data.");
                await _dataSource.ReloadAsync(_cancellationTokenSource.Token);
                Interlocked.Exchange(ref _loading, 0);
            }
            else
            {
                // If we're already in the middle of loading, it's because the load took longer than the timeout window.
                // Just skip this cycle.
                // However, this indicates that a reload is taking a long time, which is bad :(
                _logger.LogWarning("Skipping reload because a reload is already underway.");
            }
        }
    }
}
