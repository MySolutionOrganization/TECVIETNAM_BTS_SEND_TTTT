using BtsGetwayService;
using Core.MSSQL.Responsitory.Interface;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BtsS10SendTTTT
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        public readonly BtsGetway _btsGetway;
        public readonly Helper _helperUlti;
        public Worker(ILogger<Worker> logger,
            BtsGetway btsGetway,
            Helper helperUlti)
        {
            _helperUlti = helperUlti;
            _btsGetway = btsGetway;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                DateTime from = _helperUlti.RoundDown(DateTime.Now, TimeSpan.FromMinutes(5));
                DateTime to = from.AddMinutes(10);
                _logger.LogInformation("Time start: {0}", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                _logger.LogInformation("Time start send data: {0}", from.ToString("dd/MM/yyyy HH:mm:ss"));
                var tasks = new List<Task>();
                tasks.Add(Task.Run(() => _btsGetway.SendFile(to, from)));
                tasks.Add(Task.Delay(600000, stoppingToken));
                Task t = Task.WhenAll(tasks);
                await t;
            }
        }
        public override async Task<Task> StartAsync(CancellationToken cancellationToken)
        {
            var dateTimeNow = DateTime.Now;
            var milisecondDelay = _helperUlti.ThoiGianDelayDeBatDauChayService(dateTimeNow, 0.3);
            _logger.LogInformation("Worker starting at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            await Task.Delay(milisecondDelay);
            return base.StartAsync(cancellationToken);
        }
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Worker stopping at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm"));
            return base.StopAsync(cancellationToken);
        }
    }
}
