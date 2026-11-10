using BtsGetwayService;
using Core.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BtsS10SendTTTT
{
    public class Worker : BackgroundService
    {
        /// <summary>Chu kỳ xuất file.</summary>
        private static readonly TimeSpan ChuKy = TimeSpan.FromMinutes(10);
        /// <summary>
        /// Thời gian chờ sau mốc chu kỳ trước khi đọc DB, để trạm kịp gửi bản tin về.
        /// Bản cũ dùng 0.3 phút (18 giây) nên cửa sổ truy vấn gần như rỗng.
        /// </summary>
        private static readonly TimeSpan AnHan = TimeSpan.FromMinutes(1);

        private readonly ILogger<Worker> _logger;
        private readonly ILoggingService _loggingService;
        private readonly BtsGetway _btsGetway;

        public Worker(ILogger<Worker> logger,
            ILoggingService loggingService,
            BtsGetway btsGetway)
        {
            _btsGetway = btsGetway;
            _loggingService = loggingService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Canh mốc chu kỳ ở đây chứ không phải trong StartAsync: StartAsync chặn thì
                // Windows SCM sẽ coi service là "failed to start" sau 30 giây.
                var delay = ReportTimeWindow.DelayToiMocTiepTheo(DateTime.Now, ChuKy, AnHan);
                _logger.LogInformation(
                    "Cho {GiaySecond:F0}s de canh moc chu ky, bat dau luc {MocBatDau:dd/MM/yyyy HH:mm:ss}.",
                    delay.TotalSeconds, DateTime.Now.Add(delay));
                await Task.Delay(delay, stoppingToken);

                using (var timer = new PeriodicTimer(ChuKy))
                {
                    do
                    {
                        await ChayMotChuKy(stoppingToken);
                    }
                    while (await timer.WaitForNextTickAsync(stoppingToken));
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Worker da dung theo yeu cau.");
            }
        }

        private async Task ChayMotChuKy(CancellationToken stoppingToken)
        {
            try
            {
                var window = ReportTimeWindow.ChuKyLienTruoc(DateTime.Now, ChuKy);
                _logger.LogInformation("Lay du lieu chu ky {Window}.", window);
                await _btsGetway.SendFile(window, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Lỗi của một chu kỳ không được làm dừng hẳn service.
                _loggingService.Error(ex);
                _logger.LogError(ex, "Loi khi chay chu ky xuat file TTTT.");
            }
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            // Trả về ngay, không chặn. Việc canh mốc chu kỳ nằm trong ExecuteAsync.
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Worker stopping at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm"));
            return base.StopAsync(cancellationToken);
        }
    }
}
