using BtsGetwayService;
using Core.Logging;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BtsWatecService
{
    public class Worker : BackgroundService
    {
        /// <summary>Chu kỳ phát số liệu.</summary>
        private static readonly TimeSpan ChuKy = TimeSpan.FromMinutes(10);
        /// <summary>
        /// Thời gian chờ sau mốc chu kỳ trước khi đọc DB, để trạm kịp gửi bản tin về.
        /// Cửa sổ truy vấn là chu kỳ LIỀN TRƯỚC nên đây chỉ là ân hạn cho độ trễ ghi DB.
        /// </summary>
        private static readonly TimeSpan AnHan = TimeSpan.FromMinutes(1);

        private readonly ILogger<Worker> _logger;
        private readonly IGroupData _groupData;
        private readonly BtsGetway _btsGetway;
        private readonly Helper _helperUlti;
        private readonly ILoggingService _loggingService;
        private readonly AppApiWatecSetting _appSetting;
        private int _groupID;

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
                await _btsGetway.SendFile(window, _groupID, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Lỗi của một chu kỳ không được làm dừng hẳn service.
                _loggingService.Error(ex);
                _logger.LogError(ex, "Loi khi chay chu ky phat so lieu.");
            }
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker starting at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm:ss"));

            if (_appSetting.IsChooseGroup == 1)
                _groupID = ChonGroupThuCong();

            // Trả về ngay, không chặn. Việc canh mốc chu kỳ nằm trong ExecuteAsync.
            return base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Hỏi người dùng chọn group. Chỉ dùng được khi chạy dạng console;
        /// chạy dạng Windows Service thì Console.ReadLine() sẽ treo vĩnh viễn.
        /// </summary>
        private int ChonGroupThuCong()
        {
            if (!Environment.UserInteractive)
            {
                _logger.LogWarning(
                    "IsChooseGroup=1 nhung dang chay dang Windows Service (khong co console). " +
                    "Bo qua buoc chon group, se phat cho TAT CA group.");
                _loggingService.Warn(
                    "IsChooseGroup=1 khong dung duoc khi chay dang Windows Service. Dang phat cho tat ca group.");
                return 0;
            }

            try
            {
                var dsGroups = _groupData.GetAll().ToList();
                _logger.LogInformation("Vui long chon gia tri group can gui");
                foreach (var item in dsGroups)
                    _logger.LogInformation("Gia tri: ({0}) \t Ten: {1}", item.Id, _helperUlti.CreateTitle(item.Name));

                _logger.LogInformation("Nhap Gia tri: ");
                var nhap = Console.ReadLine();
                if (int.TryParse(nhap, out int groupId) && groupId != 0)
                    return groupId;

                _logger.LogWarning("Gia tri group khong hop le, se phat cho TAT CA group.");
                return 0;
            }
            catch (Exception ex)
            {
                _loggingService.Error(ex);
                _logger.LogError(ex, "Khong chon duoc group, se phat cho TAT CA group.");
                return 0;
            }
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Worker stopping at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm"));
            return base.StopAsync(cancellationToken);
        }
    }
}
