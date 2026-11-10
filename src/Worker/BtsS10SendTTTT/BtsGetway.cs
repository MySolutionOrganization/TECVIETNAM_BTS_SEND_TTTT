using bts.udpgateway;
using BtsGetwayService.MSSQL.Entity;
using Core.Logging;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BtsGetwayService
{
    /// <summary>
    /// Mỗi chu kỳ: với từng group cần gửi, lấy số liệu S10, ghi ra file JSON rồi tải lên FTP của TTTT.
    /// </summary>
    public class BtsGetway
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private readonly IGroupData _groupData;
        private readonly ISiteData _siteData;
        private readonly IReportS10Data _reportS10Data;
        private readonly IFtpUploader _ftpUploader;
        private readonly ILoggingService _loggingService;
        private readonly ILogger<BtsGetway> _logger;
        private readonly Helper _helperUlti;
        private readonly AppSetting _appSetting;

        public BtsGetway(
            IOptions<AppSetting> option,
            IGroupData groupData,
            ISiteData siteData,
            IReportS10Data reportS10Data,
            IFtpUploader ftpUploader,
            ILoggingService loggingService,
            ILogger<BtsGetway> logger,
            Helper helperUlti)
        {
            _appSetting = option.Value;
            _groupData = groupData;
            _siteData = siteData;
            _reportS10Data = reportS10Data;
            _ftpUploader = ftpUploader;
            _loggingService = loggingService;
            _logger = logger;
            _helperUlti = helperUlti;
        }

        public async Task SendFile(ReportTimeWindow window, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Bat dau chu ky {Window}.", window);

            List<RegionalGroup> lstGroup;
            try
            {
                lstGroup = _groupData.GetGroupSend().ToList();
            }
            catch (Exception ex)
            {
                _loggingService.Error(ex);
                _logger.LogError(ex, "Khong lay duoc danh sach group can gui.");
                return;
            }

            foreach (var grp in lstGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await XuLyMotGroup(grp, window, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Một group hỏng không được làm chết các group còn lại, và tuyệt đối không
                    // được thoát ra ExecuteAsync: tu .NET 6 exception o do se dung ca host.
                    _loggingService.Error(ex);
                    _logger.LogError(ex, "Loi khi xu ly group {GroupName}.", grp?.Name);
                }
            }

            _logger.LogInformation("Ket thuc chu ky {Window}.", window);
        }

        private async Task XuLyMotGroup(RegionalGroup grp, ReportTimeWindow window, CancellationToken cancellationToken)
        {
            // Bản cũ gọi .Trim() trước khi kiểm tra rỗng -> NullReferenceException khi DB để NULL,
            // và vì không ai bắt nên cả service tắt.
            var (host, port) = FtpUploader.TachHostVaPort(grp.FtpIp);
            var target = new FtpTarget
            {
                Host = host,
                Port = port,
                UserName = grp.FtpAccount?.Trim() ?? "",
                Password = grp.FtpPassword?.Trim() ?? "",
                RootDirectory = grp.FtpDirectory?.Trim() ?? ""
            };

            if (!target.HopLe)
            {
                _logger.LogWarning("Group {GroupName} chua cau hinh day du thong tin FTP, bo qua.", grp.Name);
                return;
            }

            var collected = LaySoLieu(grp, window);
            if (collected.SoTramCoDuLieu == 0)
            {
                _logger.LogInformation(
                    "Group {GroupName}: khong co tram nao co du lieu trong {Window}, khong tao file.",
                    grp.Name, window);
                return;
            }

            if (collected.TramKhongCoDuLieu.Count > 0)
            {
                var danhSach = string.Join(", ", collected.TramKhongCoDuLieu);
                _logger.LogWarning(
                    "Group {GroupName}: {SoTram} tram khong co du lieu trong {Window}: {DanhSach}",
                    grp.Name, collected.TramKhongCoDuLieu.Count, window, danhSach);
                _loggingService.Warn(
                    $"Group {grp.Name}: {collected.TramKhongCoDuLieu.Count} tram khong co du lieu: {danhSach}");
            }

            string thuMucNgay = _helperUlti.GetDay(collected.MocThoiGian);
            string thuMucLocal = GhepThuMucLocal(_appSetting.FolderLuuTruFile, target.RootDirectory, thuMucNgay);
            Directory.CreateDirectory(thuMucLocal);

            string thuMucFtp = target.RootDirectory.TrimEnd('/') + "/" + thuMucNgay + "/";
            bool guiLenTTTT = _appSetting.IsSendTTTT == 1;

            foreach (var nhom in collected.CacNhomCoDuLieu())
            {
                cancellationToken.ThrowIfCancellationRequested();

                string tenFile = $"{grp.Code}_{nhom.MaLoai}_{collected.DateTimeStr}.json";
                string duongDanLocal = Path.Combine(thuMucLocal, tenFile);

                if (!GhiFile(duongDanLocal, nhom, grp))
                    continue;

                if (!guiLenTTTT)
                {
                    _logger.LogInformation("IsSendTTTT = 0, chi ghi file {TenFile}, khong gui FTP.", tenFile);
                    continue;
                }

                await _ftpUploader.UploadAsync(target, thuMucFtp, tenFile, duongDanLocal, cancellationToken);
            }
        }

        /// <summary>
        /// Ghép thư mục lưu file cục bộ.
        ///
        /// FtpDirectory trong DB thường bắt đầu bằng '/' (vì bản cũ ghép thẳng vào URL
        /// "ftp://host" + thư mục). Path.Combine sẽ VỨT BỎ các đoạn đứng trước nếu đoạn sau
        /// bắt đầu bằng dấu phân cách, nên phải cắt dấu đầu đi trước khi ghép, nếu không
        /// file sẽ bị ghi ra ngoài FolderLuuTruFile.
        /// </summary>
        public static string GhepThuMucLocal(string thuMucGoc, string thuMucNhom, string thuMucNgay)
        {
            string goc = thuMucGoc ?? "";
            string nhom = (thuMucNhom ?? "").Trim().TrimStart('/', '\\');
            string ngay = (thuMucNgay ?? "").Trim().TrimStart('/', '\\');
            return Path.Combine(goc, nhom, ngay);
        }

        private S10CollectResult LaySoLieu(RegionalGroup grp, ReportTimeWindow window)
        {
            // Giữ nguyên GetListSite (lấy mọi loại trạm, đúng thứ tự cũ) vì thứ tự duyệt
            // quyết định timestamp đặt tên file - xem chú thích trong S10CollectResult.
            var lstSite = _siteData.GetListSite(grp.Id).ToList();
            if (lstSite.Count == 0)
                return S10DataCollector.Build(lstSite, new Dictionary<int, ReportS10>(), window);

            var dsDeviceId = lstSite.Where(s => s.DeviceId.HasValue).Select(s => s.DeviceId.Value);

            // Một query cho cả group thay vì mỗi trạm một lần gọi DB.
            var banTinTheoDevice = _reportS10Data
                .GetLatestInRange(window.From, window.To, dsDeviceId)
                .Where(r => r.DeviceId.HasValue)
                .GroupBy(r => r.DeviceId.Value)
                .ToDictionary(g => g.Key, g => g.First());

            return S10DataCollector.Build(lstSite, banTinTheoDevice, window);
        }

        /// <summary>
        /// Ghi file JSON. Bản cũ dùng if (!File.Exists) nên khi trùng tên thì bỏ qua lặng lẽ
        /// rồi vẫn tải file cũ lên FTP; ở đây luôn ghi đè để nội dung gửi đi khớp số liệu vừa lấy.
        /// </summary>
        private bool GhiFile(string duongDan, NhomFileS10 nhom, RegionalGroup grp)
        {
            try
            {
                string json = JsonSerializer.Serialize(nhom.Data, nhom.Data.GetType(), JsonOptions);
                File.WriteAllText(duongDan, json);
                _logger.LogInformation(
                    "Group {GroupName}: da ghi {SoBanGhi} ban ghi {MaLoai} vao {DuongDan}.",
                    grp.Name, nhom.SoBanGhi, nhom.MaLoai, duongDan);
                return true;
            }
            catch (Exception ex)
            {
                _loggingService.Error(ex);
                _logger.LogError(ex, "Khong ghi duoc file {DuongDan}.", duongDan);
                return false;
            }
        }
    }
}
