using bts.udpgateway;
using BtsGetwayService;
using BtsGetwayService.MSSQL.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BtsS10SendTTTT.Tests
{
    /// <summary>
    /// Test thuần cho phần gom số liệu S10 - không cần DB, FTP hay file.
    /// </summary>
    public class S10DataCollectorTests
    {
        private static readonly ReportTimeWindow Window = new ReportTimeWindow(
            new DateTime(2026, 11, 10, 10, 0, 0),
            new DateTime(2026, 11, 10, 10, 10, 0));

        private static Site Tram(int deviceId, int typeSiteId, string name = null)
        {
            return new Site
            {
                Name = name ?? $"Tram {deviceId}",
                DeviceId = deviceId,
                TypeSiteId = typeSiteId
            };
        }

        private static ReportS10 BanTin(int deviceId, DateTime dateCreate)
        {
            return new ReportS10 { DeviceId = deviceId, DateCreate = dateCreate };
        }

        private static Dictionary<int, ReportS10> BanTins(params ReportS10[] items)
        {
            return items.ToDictionary(i => i.DeviceId.Value, i => i);
        }

        [Fact]
        public void Phan_loai_dung_tung_loai_tram()
        {
            var sites = new[]
            {
                Tram(1, Constant.KhiTuong),
                Tram(2, Constant.ThuyVan),
                Tram(3, Constant.DoMua),
                Tram(4, Constant.DoGio),
                Tram(5, Constant.MuaNhiet),
            };
            var moc = new DateTime(2026, 11, 10, 10, 5, 0);
            var banTin = BanTins(sites.Select(s => BanTin(s.DeviceId.Value, moc)).ToArray());

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Single(result.KhiTuong);
            Assert.Single(result.ThuyVan);
            Assert.Single(result.Mua);
            Assert.Single(result.Gio);
            Assert.Single(result.MuaNhiet);
            Assert.Equal(5, result.SoTramCoDuLieu);
            Assert.Empty(result.TramKhongCoDuLieu);
        }

        [Fact]
        public void Tram_khong_co_ban_tin_thi_vao_danh_sach_khong_co_du_lieu()
        {
            var sites = new[] { Tram(1, Constant.DoMua, "Tram A"), Tram(2, Constant.DoMua, "Tram B") };
            var banTin = BanTins(BanTin(1, new DateTime(2026, 11, 10, 10, 5, 0)));

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Single(result.Mua);
            Assert.Equal(new[] { "Tram B" }, result.TramKhongCoDuLieu);
            Assert.Equal(1, result.SoTramCoDuLieu);
        }

        [Fact]
        public void Tram_chua_gan_DeviceId_thi_coi_nhu_khong_co_du_lieu()
        {
            var sites = new[] { new Site { Name = "Tram chua gan", DeviceId = null, TypeSiteId = Constant.DoMua } };

            var result = S10DataCollector.Build(sites, new Dictionary<int, ReportS10>(), Window);

            Assert.Equal(new[] { "Tram chua gan" }, result.TramKhongCoDuLieu);
            Assert.Equal(0, result.SoTramCoDuLieu);
        }

        [Fact]
        public void Ban_tin_thieu_DateCreate_thi_coi_nhu_khong_co_du_lieu()
        {
            var sites = new[] { Tram(1, Constant.DoMua, "Tram A") };
            var banTin = new Dictionary<int, ReportS10>
            {
                { 1, new ReportS10 { DeviceId = 1, DateCreate = null } }
            };

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Empty(result.Mua);
            Assert.Equal(new[] { "Tram A" }, result.TramKhongCoDuLieu);
        }

        /// <summary>
        /// Hành vi cũ được giữ nguyên có chủ đích: timestamp đặt tên file lấy từ trạm CUỐI CÙNG
        /// có dữ liệu, bất kể loại trạm. Test này khoá hành vi lại để không ai vô tình đổi.
        /// </summary>
        [Fact]
        public void DateTimeStr_lay_tu_tram_cuoi_cung_co_du_lieu_bat_ke_loai()
        {
            var sites = new[]
            {
                Tram(1, Constant.KhiTuong),
                Tram(2, Constant.ThuyVan),
            };
            var banTin = BanTins(
                BanTin(1, new DateTime(2026, 11, 10, 10, 1, 0)),
                BanTin(2, new DateTime(2026, 11, 10, 10, 7, 30)));

            var result = S10DataCollector.Build(sites, banTin, Window);

            // Trạm thuỷ văn (deviceId 2) là trạm cuối cùng -> quyết định tên file cho cả file AWS.
            Assert.Equal("20261110100730", result.DateTimeStr);
            Assert.Equal(new DateTime(2026, 11, 10, 10, 7, 30), result.MocThoiGian);
        }

        [Fact]
        public void Tram_khong_co_du_lieu_o_cuoi_khong_lam_doi_DateTimeStr()
        {
            var sites = new[] { Tram(1, Constant.DoMua), Tram(2, Constant.DoMua) };
            var banTin = BanTins(BanTin(1, new DateTime(2026, 11, 10, 10, 3, 0)));

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Equal("20261110100300", result.DateTimeStr);
        }

        [Fact]
        public void Khong_tram_nao_co_du_lieu_thi_moc_thoi_gian_la_dau_cua_so()
        {
            var sites = new[] { Tram(1, Constant.DoMua) };

            var result = S10DataCollector.Build(sites, new Dictionary<int, ReportS10>(), Window);

            Assert.Equal(Window.From, result.MocThoiGian);
            Assert.Equal("", result.DateTimeStr);
            Assert.Empty(result.CacNhomCoDuLieu());
        }

        [Fact]
        public void StationNo_dung_dinh_dang_D5_hoac_D6_theo_loai_tram()
        {
            var moc = new DateTime(2026, 11, 10, 10, 5, 0);
            var sites = new[]
            {
                Tram(7, Constant.KhiTuong),
                Tram(8, Constant.ThuyVan),
                Tram(9, Constant.MuaNhiet),
                Tram(10, Constant.DoMua),
                Tram(11, Constant.DoGio),
            };
            var banTin = BanTins(sites.Select(s => BanTin(s.DeviceId.Value, moc)).ToArray());

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Equal("00007", result.KhiTuong[0].StationNo);
            Assert.Equal("00008", result.ThuyVan[0].StationNo);
            Assert.Equal("00009", result.MuaNhiet[0].StationNo);
            Assert.Equal("000010", result.Mua[0].StationNo);
            Assert.Equal("000011", result.Gio[0].StationNo);
        }

        [Fact]
        public void Muc_nuoc_duoc_lam_tron_2_chu_so()
        {
            var sites = new[] { Tram(1, Constant.ThuyVan) };
            var banTin = new Dictionary<int, ReportS10>
            {
                { 1, new ReportS10 { DeviceId = 1, DateCreate = new DateTime(2026, 11, 10, 10, 5, 0), MFL = 12.3456 } }
            };

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Equal(12.35f, result.ThuyVan[0].WL, 2);
        }

        [Fact]
        public void Muc_nuoc_null_thi_bang_0()
        {
            var sites = new[] { Tram(1, Constant.ThuyVan) };
            var banTin = BanTins(BanTin(1, new DateTime(2026, 11, 10, 10, 5, 0)));

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Equal(0f, result.ThuyVan[0].WL);
        }

        [Fact]
        public void Gia_tri_null_tra_ve_sentinel_9999()
        {
            var sites = new[] { Tram(1, Constant.DoMua) };
            var banTin = BanTins(BanTin(1, new DateTime(2026, 11, 10, 10, 5, 0)));

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Equal(-9999, result.Mua[0].Rain10m);
            Assert.Equal(-9999, result.Mua[0].Battery);
        }

        [Fact]
        public void Datadate_giu_nguyen_14_chu_so_khong_bi_ky_hieu_khoa_hoc()
        {
            var sites = new[] { Tram(1, Constant.DoMua) };
            var banTin = BanTins(BanTin(1, new DateTime(2026, 11, 10, 23, 59, 59)));

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Equal(20261110235959d, result.Mua[0].Datadate);
            Assert.Equal("20261110235959", result.DateTimeStr);
        }

        [Fact]
        public void CacNhomCoDuLieu_chi_tra_nhom_co_ban_ghi_dung_thu_tu()
        {
            var moc = new DateTime(2026, 11, 10, 10, 5, 0);
            var sites = new[] { Tram(1, Constant.DoMua), Tram(2, Constant.KhiTuong) };
            var banTin = BanTins(BanTin(1, moc), BanTin(2, moc));

            var result = S10DataCollector.Build(sites, banTin, Window);
            var nhom = result.CacNhomCoDuLieu().ToList();

            Assert.Equal(2, nhom.Count);
            Assert.Equal(Constant.MaKhiTuong, nhom[0].MaLoai);
            Assert.Equal(Constant.MaDoMua, nhom[1].MaLoai);
            Assert.All(nhom, n => Assert.True(n.SoBanGhi > 0));
        }

        [Fact]
        public void Loai_tram_khong_nam_trong_5_loai_thi_bo_qua()
        {
            var sites = new[] { Tram(1, 99) };
            var banTin = BanTins(BanTin(1, new DateTime(2026, 11, 10, 10, 5, 0)));

            var result = S10DataCollector.Build(sites, banTin, Window);

            Assert.Equal(0, result.SoTramCoDuLieu);
            Assert.Empty(result.CacNhomCoDuLieu());
            Assert.Equal("", result.DateTimeStr);
        }

        [Fact]
        public void Danh_sach_tram_rong_thi_khong_no()
        {
            var result = S10DataCollector.Build(new List<Site>(), new Dictionary<int, ReportS10>(), Window);

            Assert.Equal(0, result.SoTramCoDuLieu);
            Assert.Empty(result.TramKhongCoDuLieu);
        }
    }
}
