using BtsGetwayService;
using Core.Logging;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BtsWatecService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        public readonly IGroupData _groupData;
        public readonly BtsGetway _btsGetway;
        public readonly Helper _helperUlti;
        private readonly ILoggingService _loggingService;
        public readonly AppApiWatecSetting _appSetting;
        public int _groupID;
        public Worker(ILogger<Worker> logger,
            IOptions<AppApiWatecSetting> option,
            BtsGetway btsGetway,
             IGroupData groupData,
            ILoggingService loggingService,
            Helper helperUlti)
        {
            _appSetting = option.Value;
            _helperUlti = helperUlti;
            _btsGetway = btsGetway;
            _groupData = groupData;
            _loggingService = loggingService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {

                DateTime from = _helperUlti.RoundDown(DateTime.Now, TimeSpan.FromMinutes(10));
                DateTime to = from.AddMinutes(10);
                _logger.LogInformation("Time get data from: {0} to {1}", from.ToString("dd/MM/yyyy HH:mm:ss"), to.ToString("dd/MM/yyyy HH:mm:ss"));                
                var tasks = new List<Task>();
                tasks.Add(Task.Run(() => _btsGetway.SendFile(to, from, _groupID)));
                tasks.Add(Task.Delay(600000, stoppingToken));
                Task t = Task.WhenAll(tasks);
                await t;
            }
        }
        public override async Task<Task> StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            int groupId = 0;
            var dsGroups = _groupData.GetAll();
            if (_appSetting.IsChooseGroup == 1)
            {
                _logger.LogInformation("Vui long chon gia tri group can gui");
                foreach (var item in dsGroups)
                {
                    _logger.LogInformation("Gia tri: ({0}) \t Ten: {1}", item.Id, _helperUlti.CreateTitle(item.Name));
                }
                _logger.LogInformation("Nhap Gia tri: ");
                groupId = Convert.ToInt32(Console.ReadLine());
                if (groupId != 0)
                {
                    _groupID = groupId;
                    var dateTimeNow = DateTime.Now;
                    var milisecondDelay = _helperUlti.ThoiGianDelayDeBatDauChayService(dateTimeNow);
                    _logger.LogInformation("Worker delay " + milisecondDelay);
                    _logger.LogInformation("Worker starting at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                    await Task.Delay(milisecondDelay);
                    return base.StartAsync(cancellationToken);
                }
                else
                {
                    return base.StopAsync(cancellationToken);
                }
            }
            else
            {
                var dateTimeNow = DateTime.Now;
                var milisecondDelay = _helperUlti.ThoiGianDelayDeBatDauChayService(dateTimeNow, 1);                
                await Task.Delay(milisecondDelay);
                _logger.LogInformation("Starting at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss"));
                return base.StartAsync(cancellationToken);
            }

        }
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Worker stopping at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm"));
            return base.StopAsync(cancellationToken);
        }
    }
}
