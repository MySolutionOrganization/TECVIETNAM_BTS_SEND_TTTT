using bts.udpgateway;
using BtsGetwayService.MSSQL.Entity;
using Core.Helper;
using Core.Model;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BtsGetwayService
{
    /// <summary>
    /// Một trạm bị mất bản tin trong chu kỳ, kèm thông tin để cảnh báo.
    /// </summary>
    public class TramMatBanTin
    {
        public Site Site { get; set; }
        public int DeviceId { get; set; }
        public int SoBanTinMatLienTiep { get; set; }
        /// <summary>True: đây là lần đầu chạm ngưỡng, cần bắn cảnh báo.</summary>
        public bool CanCanhBao { get; set; }
    }

    /// <summary>
    /// Một trạm vừa có dữ liệu trở lại sau khi đã bị dừng phát.
    /// </summary>
    public class TramKhoiPhuc
    {
        public Site Site { get; set; }
        public int DeviceId { get; set; }
        public int SoBanTinDaMat { get; set; }
    }

    /// <summary>
    /// Kết quả thu thập số liệu mưa của một group trong một chu kỳ.
    /// </summary>
    public class RainCollectResult
    {
        /// <summary>Dữ liệu sẵn sàng gửi lên Watec (gồm cả bản tin thật và bản bù val = 0).</summary>
        public List<WatecS10Model> Payload { get; } = new List<WatecS10Model>();
        /// <summary>Tên các trạm không có bản tin trong chu kỳ.</summary>
        public List<string> TramKhongCoDuLieu { get; } = new List<string>();
        /// <summary>Các trạm được bù số liệu mưa = 0.</summary>
        public List<string> TramBuSoLieu0 { get; } = new List<string>();
        /// <summary>Các trạm đã chạm ngưỡng và bị dừng phát trong chu kỳ này.</summary>
        public List<TramMatBanTin> TramDungPhat { get; } = new List<TramMatBanTin>();
        /// <summary>Các trạm vừa có dữ liệu trở lại sau khi bị dừng phát.</summary>
        public List<TramKhoiPhuc> TramKhoiPhuc { get; } = new List<TramKhoiPhuc>();
    }

    /// <summary>
    /// Thu thập số liệu mưa của một group trong một cửa sổ thời gian và dựng payload gửi Watec.
    ///
    /// Quy tắc bù bản tin (giữ nguyên từ yêu cầu nghiệp vụ):
    /// - Mất bản tin thứ 1..(MaxMissingMessages-1): vẫn phát với val = 0.
    ///   Số liệu phải bằng 0, KHÔNG lấy lại số liệu của bản tin trước.
    /// - Mất bản tin thứ MaxMissingMessages trở đi: dừng phát cho trạm đó, cảnh báo đúng 1 lần.
    ///
    /// Lớp này chỉ đụng tới DB và bộ đếm; không biết gì về HTTP.
    /// </summary>
    public class RainDataCollector
    {
        private readonly ISiteData _siteData;
        private readonly IReportS10Data _reportS10Data;
        private readonly MissingDataTracker _missingDataTracker;
        private readonly ILogger<RainDataCollector> _logger;
        private readonly AppApiWatecSetting _appSetting;

        public RainDataCollector(
            ISiteData siteData,
            IReportS10Data reportS10Data,
            MissingDataTracker missingDataTracker,
            IOptions<AppApiWatecSetting> option,
            ILogger<RainDataCollector> logger)
        {
            _siteData = siteData;
            _reportS10Data = reportS10Data;
            _missingDataTracker = missingDataTracker;
            _appSetting = option.Value;
            _logger = logger;
        }

        /// <summary>Số bản tin mất liên tiếp thì dừng phát cho trạm đó. Mặc định 3.</summary>
        private int MaxMissingMessages =>
            _appSetting.MaxMissingMessages > 0 ? _appSetting.MaxMissingMessages : 3;

        public RainCollectResult Collect(int groupId, ReportTimeWindow window)
        {
            var result = new RainCollectResult();

            var lstSite = _siteData.GetListSiteByType(groupId, Constant.DoMua).ToList();
            if (lstSite.Count == 0)
                return result;

            // Trạm chưa gán DeviceId thì không có sid để phát về Watec.
            var siteCoDeviceId = lstSite.Where(s => s.DeviceId.HasValue).ToList();
            foreach (var site in lstSite.Where(s => !s.DeviceId.HasValue))
                result.TramKhongCoDuLieu.Add(site.Name);

            if (siteCoDeviceId.Count == 0)
                return result;

            // Một query cho cả group thay vì mỗi trạm một lần gọi DB.
            var dsBanTin = _reportS10Data
                .GetLatestInRange(window.From, window.To, siteCoDeviceId.Select(s => s.DeviceId.Value))
                .Where(r => r.DeviceId.HasValue)
                .GroupBy(r => r.DeviceId.Value)
                .ToDictionary(g => g.Key, g => g.First());

            _logger.LogInformation(
                "Group {GroupId}: {SoTram} tram do mua, {SoBanTin} tram co ban tin trong {Window}.",
                groupId, siteCoDeviceId.Count, dsBanTin.Count, window);

            foreach (var site in siteCoDeviceId)
            {
                int deviceId = site.DeviceId.Value;

                if (dsBanTin.TryGetValue(deviceId, out var banTin))
                    XuLyCoBanTin(result, site, deviceId, banTin);
                else
                    XuLyMatBanTin(result, site, deviceId, window);
            }

            return result;
        }

        private void XuLyCoBanTin(RainCollectResult result, Site site, int deviceId, ReportS10 banTin)
        {
            var recovered = _missingDataTracker.RegisterReceived(deviceId);
            if (recovered.IsRecoveryFromAlert)
            {
                result.TramKhoiPhuc.Add(new TramKhoiPhuc
                {
                    Site = site,
                    DeviceId = deviceId,
                    SoBanTinDaMat = recovered.PreviousMissing
                });
            }

            var mocThoiGian = banTin.DateCreate ?? DateTime.Now;
            result.Payload.Add(new WatecS10Model
            {
                sid = deviceId.ToString("D10"),
                from = mocThoiGian.ToString("yyyy-MM-dd HH:mm"),
                to = mocThoiGian.ToString("yyyy-MM-dd HH:mm"),
                val = Utility.CheckNull(banTin.MRT)
            });
        }

        private void XuLyMatBanTin(RainCollectResult result, Site site, int deviceId, ReportTimeWindow window)
        {
            result.TramKhongCoDuLieu.Add(site.Name);
            var missing = _missingDataTracker.RegisterMissing(deviceId, MaxMissingMessages);

            if (missing.ShouldSendZero)
            {
                // Bù bản tin với số liệu mưa = 0. KHÔNG lấy lại số liệu của bản tin trước.
                result.Payload.Add(new WatecS10Model
                {
                    sid = deviceId.ToString("D10"),
                    from = window.From.ToString("yyyy-MM-dd HH:mm"),
                    to = window.From.ToString("yyyy-MM-dd HH:mm"),
                    val = 0
                });

                result.TramBuSoLieu0.Add($"{site.Name} (mat ban tin thu {missing.ConsecutiveMissing}/{MaxMissingMessages})");
                _logger.LogWarning(
                    "Tram {SiteName} (DeviceId {DeviceId}) mat ban tin thu {SoLan}, bu so lieu mua = 0 cho moc {Moc}.",
                    site.Name, deviceId, missing.ConsecutiveMissing, window.From.ToString("dd/MM/yyyy HH:mm"));
            }
            else
            {
                // Đã mất đủ ngưỡng bản tin liên tiếp -> dừng phát cho trạm này.
                result.TramDungPhat.Add(new TramMatBanTin
                {
                    Site = site,
                    DeviceId = deviceId,
                    SoBanTinMatLienTiep = missing.ConsecutiveMissing,
                    CanCanhBao = missing.ShouldAlert
                });
                _logger.LogError(
                    "Tram {SiteName} (DeviceId {DeviceId}) mat {SoLan} ban tin lien tiep -> DUNG PHAT ve Watec.",
                    site.Name, deviceId, missing.ConsecutiveMissing);
            }
        }
    }
}
