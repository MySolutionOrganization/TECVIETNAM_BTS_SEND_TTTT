using bts.udpgateway;
using BtsGetwayService.Model;
using BtsGetwayService.MSSQL.Entity;
using Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BtsGetwayService
{
    /// <summary>
    /// Một nhóm bản ghi sẽ được xuất thành một file JSON gửi sang TTTT.
    /// </summary>
    public class NhomFileS10
    {
        /// <summary>Mã loại trạm dùng trong tên file: AWS / WIND / RAIN / RAINTEMP / WL.</summary>
        public string MaLoai { get; set; }
        /// <summary>Danh sách bản ghi, giữ nguyên kiểu cụ thể để serialize đúng.</summary>
        public object Data { get; set; }
        public int SoBanGhi { get; set; }
    }

    /// <summary>
    /// Kết quả gom số liệu S10 của một group trong một chu kỳ.
    /// </summary>
    public class S10CollectResult
    {
        public List<ModelFileKhiTuongS10Json> KhiTuong { get; } = new List<ModelFileKhiTuongS10Json>();
        public List<ModelFileGioS10Json> Gio { get; } = new List<ModelFileGioS10Json>();
        public List<ModelFileLuongMuaS10Json> Mua { get; } = new List<ModelFileLuongMuaS10Json>();
        public List<ModelFileMuaNhietS10Json> MuaNhiet { get; } = new List<ModelFileMuaNhietS10Json>();
        public List<ModelFileThuyVanS10Json> ThuyVan { get; } = new List<ModelFileThuyVanS10Json>();

        /// <summary>
        /// Timestamp dùng đặt tên cho TẤT CẢ các file của group này.
        ///
        /// GIỮ NGUYÊN HÀNH VI CŨ (đã được xác nhận là không đổi): đây là Datadate của trạm
        /// CUỐI CÙNG có dữ liệu trong vòng lặp, bất kể thuộc loại trạm nào. Nghĩa là file AWS
        /// có thể mang timestamp của một trạm thuỷ văn. Không "sửa" chỗ này nếu chưa thống nhất
        /// với phía TTTT, vì nó làm đổi tên file bên nhận.
        /// </summary>
        public string DateTimeStr { get; set; } = "";

        /// <summary>
        /// Mốc thời gian dùng chọn thư mục ngày. Cũng là DateCreate của trạm cuối cùng có dữ liệu
        /// (giữ nguyên hành vi cũ); mặc định là đầu cửa sổ nếu không trạm nào có dữ liệu.
        /// </summary>
        public DateTime MocThoiGian { get; set; }

        public int SoTramCoDuLieu { get; set; }
        public List<string> TramKhongCoDuLieu { get; } = new List<string>();

        /// <summary>Các nhóm có bản ghi, theo đúng thứ tự xuất file như bản cũ.</summary>
        public IEnumerable<NhomFileS10> CacNhomCoDuLieu()
        {
            if (KhiTuong.Count > 0)
                yield return new NhomFileS10 { MaLoai = Constant.MaKhiTuong, Data = KhiTuong, SoBanGhi = KhiTuong.Count };
            if (Gio.Count > 0)
                yield return new NhomFileS10 { MaLoai = Constant.MaDoGio, Data = Gio, SoBanGhi = Gio.Count };
            if (Mua.Count > 0)
                yield return new NhomFileS10 { MaLoai = Constant.MaDoMua, Data = Mua, SoBanGhi = Mua.Count };
            if (MuaNhiet.Count > 0)
                yield return new NhomFileS10 { MaLoai = Constant.MaMuaNhiet, Data = MuaNhiet, SoBanGhi = MuaNhiet.Count };
            if (ThuyVan.Count > 0)
                yield return new NhomFileS10 { MaLoai = Constant.MaThuyVan, Data = ThuyVan, SoBanGhi = ThuyVan.Count };
        }
    }

    /// <summary>
    /// Chuyển danh sách trạm + bản tin S10 đã lấy sẵn thành các nhóm bản ghi để xuất file.
    ///
    /// Hàm thuần: không đụng DB, không đụng file, không đụng FTP - nên test được offline.
    /// </summary>
    public static class S10DataCollector
    {
        public static S10CollectResult Build(
            IEnumerable<Site> sites,
            IReadOnlyDictionary<int, ReportS10> banTinTheoDevice,
            ReportTimeWindow window)
        {
            var result = new S10CollectResult { MocThoiGian = window.From };
            if (sites == null)
                return result;

            foreach (var site in sites)
            {
                if (!site.DeviceId.HasValue)
                {
                    result.TramKhongCoDuLieu.Add(site.Name);
                    continue;
                }

                int deviceId = site.DeviceId.Value;
                if (banTinTheoDevice == null
                    || !banTinTheoDevice.TryGetValue(deviceId, out var item)
                    || item == null
                    || !item.DateCreate.HasValue)
                {
                    result.TramKhongCoDuLieu.Add(site.Name);
                    continue;
                }

                double datadate = ToDatadate(item.DateCreate.Value);
                bool daThem = true;

                if (site.TypeSiteId == Constant.KhiTuong)
                    result.KhiTuong.Add(MapKhiTuong(item, deviceId, datadate));
                else if (site.TypeSiteId == Constant.ThuyVan)
                    result.ThuyVan.Add(MapThuyVan(item, deviceId, datadate));
                else if (site.TypeSiteId == Constant.DoMua)
                    result.Mua.Add(MapMua(item, deviceId, datadate));
                else if (site.TypeSiteId == Constant.DoGio)
                    result.Gio.Add(MapGio(item, deviceId, datadate));
                else if (site.TypeSiteId == Constant.MuaNhiet)
                    result.MuaNhiet.Add(MapMuaNhiet(item, deviceId, datadate));
                else
                    daThem = false;

                if (daThem)
                {
                    result.SoTramCoDuLieu++;
                    // Giữ nguyên hành vi cũ: trạm cuối cùng có dữ liệu quyết định tên file
                    // và thư mục ngày cho cả group.
                    result.DateTimeStr = datadate.ToString("F0", CultureInfo.InvariantCulture);
                    result.MocThoiGian = item.DateCreate.Value;
                }
            }

            return result;
        }

        /// <summary>yyyyMMddHHmmss dưới dạng số, theo đúng định dạng file TTTT đang nhận.</summary>
        private static double ToDatadate(DateTime dateCreate)
        {
            return double.Parse(
                dateCreate.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture);
        }

        private static ModelFileKhiTuongS10Json MapKhiTuong(ReportS10 item, int deviceId, double datadate)
        {
            return new ModelFileKhiTuongS10Json
            {
                StationNo = deviceId.ToString("D5"),
                Datadate = datadate,
                T2m = Utility.CheckNull(item.MTI),
                T2mmax = Utility.CheckNull(item.MTX),
                T2mmin = Utility.CheckNull(item.MTM),
                Rh2m = Utility.CheckNull(item.MHU),
                R2mmax = Utility.CheckNull(item.MHX),
                R2mmin = Utility.CheckNull(item.MHM),
                PS = Utility.CheckNull(item.MAV),
                FF10m = Utility.CheckNull(item.MSM),
                DD10m = Utility.CheckNull(item.MDM),
                FxFx = Utility.CheckNull(item.MSS),
                DxDx = Utility.CheckNull(item.MDS),
                DtdateFxDx = Utility.CheckNull(item.MHR),
                Rain10m = Utility.CheckNull(item.MRT),
                Rain1h = Utility.CheckNull(item.MRH),
                Rain19h = Utility.CheckNull(item.MRC),
                Rain00h = Utility.CheckNull(item.MRB),
                Battery = Utility.CheckNull(item.MVC)
            };
        }

        private static ModelFileThuyVanS10Json MapThuyVan(ReportS10 item, int deviceId, double datadate)
        {
            return new ModelFileThuyVanS10Json
            {
                StationNo = deviceId.ToString("D5"),
                Datadate = datadate,
                // Bản cũ round-trip qua string (float.Parse(Math.Round(x,2).ToString())) nên phụ
                // thuộc culture của máy chạy. Ép kiểu trực tiếp cho kết quả giống hệt mà an toàn.
                WL = item.MFL.HasValue ? (float)Math.Round(item.MFL.Value, 2) : 0f
            };
        }

        private static ModelFileLuongMuaS10Json MapMua(ReportS10 item, int deviceId, double datadate)
        {
            return new ModelFileLuongMuaS10Json
            {
                StationNo = deviceId.ToString("D6"),
                Datadate = datadate,
                Rain10m = Utility.CheckNull(item.MRT),
                Rain1h = Utility.CheckNull(item.MRH),
                Rain19h = Utility.CheckNull(item.MRC),
                Rain00h = Utility.CheckNull(item.MRB),
                Battery = Utility.CheckNull(item.MVC)
            };
        }

        private static ModelFileGioS10Json MapGio(ReportS10 item, int deviceId, double datadate)
        {
            return new ModelFileGioS10Json
            {
                StationNo = deviceId.ToString("D6"),
                Datadate = datadate,
                FF10m = Utility.CheckNull(item.MSM),
                DD10m = Utility.CheckNull(item.MDM),
                FxFx = Utility.CheckNull(item.MSS),
                DxDx = Utility.CheckNull(item.MDS),
                DtdateFxDx = Utility.CheckNull(item.MHR),
                Battery = Utility.CheckNull(item.MVC)
            };
        }

        private static ModelFileMuaNhietS10Json MapMuaNhiet(ReportS10 item, int deviceId, double datadate)
        {
            return new ModelFileMuaNhietS10Json
            {
                StationNo = deviceId.ToString("D5"),
                Datadate = datadate,
                T2m = Utility.CheckNull(item.MTI),
                T2mmax = Utility.CheckNull(item.MTX),
                T2mmin = Utility.CheckNull(item.MTM),
                Rh2m = Utility.CheckNull(item.MHU),
                R2mmax = Utility.CheckNull(item.MHX),
                R2mmin = Utility.CheckNull(item.MHM),
                Rain10m = Utility.CheckNull(item.MRT),
                Rain1h = Utility.CheckNull(item.MRH),
                Rain19h = Utility.CheckNull(item.MRC),
                Rain00h = Utility.CheckNull(item.MRB),
                Battery = Utility.CheckNull(item.MVC)
            };
        }
    }
}
