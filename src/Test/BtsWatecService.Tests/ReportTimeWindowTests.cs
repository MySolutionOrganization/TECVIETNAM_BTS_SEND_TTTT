using BtsGetwayService;
using System;
using Xunit;

namespace BtsWatecService.Tests
{
    /// <summary>
    /// Kiểm tra cửa sổ truy vấn là chu kỳ LIỀN TRƯỚC (đã đóng), không phải chu kỳ hiện tại.
    /// Test thuần, không cần DB.
    /// </summary>
    public class ReportTimeWindowTests
    {
        private static readonly TimeSpan ChuKy = TimeSpan.FromMinutes(10);

        [Fact]
        public void Chay_luc_10h11_thi_lay_cua_so_10h00_den_10h10()
        {
            var now = new DateTime(2026, 11, 10, 10, 11, 0);

            var window = ReportTimeWindow.ChuKyLienTruoc(now, ChuKy);

            Assert.Equal(new DateTime(2026, 11, 10, 10, 0, 0), window.From);
            Assert.Equal(new DateTime(2026, 11, 10, 10, 10, 0), window.To);
        }

        [Fact]
        public void Cua_so_luon_la_chu_ky_da_dong_nen_To_khong_vuot_qua_hien_tai()
        {
            var now = new DateTime(2026, 11, 10, 10, 19, 59);

            var window = ReportTimeWindow.ChuKyLienTruoc(now, ChuKy);

            Assert.True(window.To <= now);
            Assert.Equal(ChuKy, window.To - window.From);
        }

        [Fact]
        public void Dung_mocchu_ky_thi_lay_chu_ky_truoc_do()
        {
            var now = new DateTime(2026, 11, 10, 10, 10, 0);

            var window = ReportTimeWindow.ChuKyLienTruoc(now, ChuKy);

            Assert.Equal(new DateTime(2026, 11, 10, 10, 0, 0), window.From);
            Assert.Equal(new DateTime(2026, 11, 10, 10, 10, 0), window.To);
        }

        [Fact]
        public void Qua_nua_dem_thi_lay_cua_so_cuoi_ngay_hom_truoc()
        {
            var now = new DateTime(2026, 11, 10, 0, 1, 0);

            var window = ReportTimeWindow.ChuKyLienTruoc(now, ChuKy);

            Assert.Equal(new DateTime(2026, 11, 9, 23, 50, 0), window.From);
            Assert.Equal(new DateTime(2026, 11, 10, 0, 0, 0), window.To);
        }

        [Fact]
        public void Cac_cua_so_lien_tiep_khong_chong_lan_va_khong_bo_sot()
        {
            var w1 = ReportTimeWindow.ChuKyLienTruoc(new DateTime(2026, 11, 10, 10, 11, 0), ChuKy);
            var w2 = ReportTimeWindow.ChuKyLienTruoc(new DateTime(2026, 11, 10, 10, 21, 0), ChuKy);

            Assert.Equal(w1.To, w2.From);
        }

        [Fact]
        public void Delay_canh_dung_moc_chu_ky_cong_an_han()
        {
            var now = new DateTime(2026, 11, 10, 10, 3, 0);

            var delay = ReportTimeWindow.DelayToiMocTiepTheo(now, ChuKy, TimeSpan.FromMinutes(1));

            // Mốc tiếp theo 10:10, cộng 1 phút ân hạn -> 10:11, cách 10:03 là 8 phút.
            Assert.Equal(TimeSpan.FromMinutes(8), delay);
        }

        [Fact]
        public void Delay_nhay_sang_moc_sau_neu_da_qua_moc_cong_an_han()
        {
            var now = new DateTime(2026, 11, 10, 10, 10, 30);

            var delay = ReportTimeWindow.DelayToiMocTiepTheo(now, ChuKy, TimeSpan.FromMinutes(0));

            // 10:10 + 0 ân hạn đã qua -> mốc kế tiếp 10:20, cách 10:10:30 là 9 phút 30 giây.
            Assert.Equal(TimeSpan.FromSeconds(570), delay);
        }

        [Fact]
        public void Delay_luon_duong()
        {
            var rnd = new Random(42);
            for (int i = 0; i < 200; i++)
            {
                var now = new DateTime(2026, 11, 10, rnd.Next(24), rnd.Next(60), rnd.Next(60));
                var delay = ReportTimeWindow.DelayToiMocTiepTheo(now, ChuKy, TimeSpan.FromMinutes(1));
                Assert.True(delay > TimeSpan.Zero, $"Delay phai duong, now = {now:HH:mm:ss}, delay = {delay}");
                Assert.True(delay <= ChuKy, $"Delay khong duoc vuot qua mot chu ky, now = {now:HH:mm:ss}, delay = {delay}");
            }
        }

        [Fact]
        public void Chu_ky_khong_hop_le_thi_nem_loi()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ReportTimeWindow.ChuKyLienTruoc(DateTime.Now, TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ReportTimeWindow.DelayToiMocTiepTheo(DateTime.Now, TimeSpan.Zero, TimeSpan.Zero));
        }
    }
}
