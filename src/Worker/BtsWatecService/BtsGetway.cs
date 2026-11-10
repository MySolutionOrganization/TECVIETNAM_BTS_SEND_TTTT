using BtsGetwayService.MSSQL.Entity;
using Core.Logging;
using Core.MSSQL.Responsitory.Interface;
using Core.PushMessage;
using Core.Setting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BtsGetwayService
{
    /// <summary>
    /// Điều phối một chu kỳ phát số liệu: với mỗi group, thu thập số liệu rồi gửi lên Watec
    /// và log tổng hợp. Không chứa SQL, không chứa HTTP.
    /// </summary>
    public class BtsGetway
    {
        private readonly ILogger<BtsGetway> _logger;
        private readonly IGroupData _groupData;
        private readonly ILoggingService _loggingService;
        private readonly RainDataCollector _collector;
        private readonly IWatecApiClient _apiClient;
        private readonly IPushMessageService _pushMessageService;
        private readonly AppApiWatecSetting _appSetting;

        public BtsGetway(
            ILogger<BtsGetway> logger,
            IOptions<AppApiWatecSetting> option,
            IGroupData groupData,
            ILoggingService loggingService,
            RainDataCollector collector,
            IWatecApiClient apiClient,
            IPushMessageService pushMessageService)
        {
            _appSetting = option.Value;
            _groupData = groupData;
            _loggingService = loggingService;
            _collector = collector;
            _apiClient = apiClient;
            _pushMessageService = pushMessageService;
            _logger = logger;
        }

        public async Task SendFile(ReportTimeWindow window, int groupId, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Bat dau chu ky {Window}.", window);

            List<RegionalGroup> lstGroup;
            try
            {
                var lstGroupAll = _groupData.GetAll().ToList();
                lstGroup = _appSetting.IsChooseGroup == 1
                    ? lstGroupAll.Where(i => i.Id == groupId).ToList()
                    : lstGroupAll;
            }
            catch (Exception ex)
            {
                _loggingService.Error(ex);
                _logger.LogError(ex, "Khong lay duoc danh sach group.");
                return;
            }

            foreach (var grp in lstGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await XuLyMotGroup(grp, window, cancellationToken);
            }

            _logger.LogInformation("Ket thuc chu ky {Window}.", window);
        }

        private async Task XuLyMotGroup(RegionalGroup grp, ReportTimeWindow window, CancellationToken cancellationToken)
        {
            RainCollectResult collected;
            try
            {
                collected = _collector.Collect(grp.Id, window);
            }
            catch (Exception ex)
            {
                // Một group lỗi không được làm hỏng các group còn lại.
                _loggingService.Error(ex);
                _logger.LogError(ex, "Loi khi thu thap so lieu cho group {GroupName}.", grp.Name);
                return;
            }

            GhiLogTongHop(grp, window, collected);

            foreach (var tram in collected.TramKhoiPhuc)
                await CanhBaoKhoiPhuc(tram);

            foreach (var tram in collected.TramDungPhat.Where(t => t.CanCanhBao))
                await CanhBaoMatBanTin(grp, tram, window);

            if (collected.Payload.Count == 0)
            {
                _logger.LogInformation("Khong co du lieu de gui cho group {GroupName}.", grp.Name);
                return;
            }

            var result = await _apiClient.SendAsync(collected.Payload, cancellationToken);
            if (result.Success)
            {
                _logger.LogInformation(
                    "Da gui {SoBanGhi} ban ghi cho group {GroupName} sau {SoLan} lan thu. HTTP {StatusCode}.",
                    collected.Payload.Count, grp.Name, result.AttemptCount, result.StatusCode);
                _loggingService.Info(
                    $"Da gui {collected.Payload.Count} ban ghi cho group {grp.Name}. HTTP {result.StatusCode}");
            }
            else
            {
                _logger.LogError(
                    "Gui that bai {SoBanGhi} ban ghi cho group {GroupName} sau {SoLan} lan thu. {StatusCode}: {Message}",
                    collected.Payload.Count, grp.Name, result.AttemptCount, result.StatusCode, result.Message);
            }
        }

        private void GhiLogTongHop(RegionalGroup grp, ReportTimeWindow window, RainCollectResult collected)
        {
            if (collected.TramKhongCoDuLieu.Count > 0)
            {
                var danhSach = string.Join(", ", collected.TramKhongCoDuLieu);
                _logger.LogWarning(
                    "Group {GroupName}: {SoTram} tram khong co du lieu trong {Window}: {DanhSach}",
                    grp.Name, collected.TramKhongCoDuLieu.Count, window, danhSach);
                _loggingService.Warn(
                    $"Group {grp.Name}: {collected.TramKhongCoDuLieu.Count} tram khong co du lieu: {danhSach}");
            }

            if (collected.TramBuSoLieu0.Count > 0)
            {
                var danhSach = string.Join(", ", collected.TramBuSoLieu0);
                _loggingService.Warn(
                    $"Group {grp.Name}: {collected.TramBuSoLieu0.Count} tram duoc bu so lieu mua = 0: {danhSach}");
            }

            if (collected.TramDungPhat.Count > 0)
            {
                var danhSach = string.Join(", ",
                    collected.TramDungPhat.Select(t => $"{t.Site.Name} (mat {t.SoBanTinMatLienTiep} ban tin lien tiep)"));
                _loggingService.Fatal(
                    $"Group {grp.Name}: {collected.TramDungPhat.Count} tram da dung phat ve Watec: {danhSach}");
            }
        }

        /// <summary>
        /// Cảnh báo trạm mất bản tin liên tiếp đến ngưỡng, đã dừng phát về Watec.
        /// </summary>
        private async Task CanhBaoMatBanTin(RegionalGroup grp, TramMatBanTin tram, ReportTimeWindow window)
        {
            var noiDung = $"🔴 DUNG PHAT WATEC\n"
                + $"Tram: {tram.Site.Name}\n"
                + $"DeviceId: {tram.DeviceId}\n"
                + $"Khu vuc: {grp.Name}\n"
                + $"Da mat {tram.SoBanTinMatLienTiep} ban tin lien tiep (den {window.To:dd/MM/yyyy HH:mm}).\n"
                + $"Da ngung phat so lieu ve Watec cho tram nay.";
            await GuiCanhBao(noiDung);
            _loggingService.Fatal(noiDung.Replace("\n", " | "));
        }

        /// <summary>
        /// Cảnh báo trạm đã có dữ liệu trở lại sau khi bị dừng phát.
        /// </summary>
        private async Task CanhBaoKhoiPhuc(TramKhoiPhuc tram)
        {
            var noiDung = $"🟢 PHAT LAI WATEC\n"
                + $"Tram: {tram.Site.Name}\n"
                + $"DeviceId: {tram.DeviceId}\n"
                + $"Da co du lieu tro lai sau {tram.SoBanTinDaMat} ban tin bi mat. Tiep tuc phat ve Watec.";
            await GuiCanhBao(noiDung);
            _loggingService.Info(noiDung.Replace("\n", " | "));
            _logger.LogInformation(
                "Tram {SiteName} (DeviceId {DeviceId}) da co du lieu tro lai, phat lai ve Watec.",
                tram.Site.Name, tram.DeviceId);
        }

        /// <summary>
        /// Telegram lỗi không được làm hỏng luồng phát số liệu.
        /// </summary>
        private async Task GuiCanhBao(string noiDung)
        {
            try
            {
                await _pushMessageService.SendMessageAsync(noiDung);
            }
            catch (Exception ex)
            {
                _loggingService.Error(ex);
                _logger.LogError(ex, "Khong gui duoc canh bao Telegram.");
            }
        }
    }
}
