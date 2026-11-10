using BtsGetwayService;
using Xunit;

namespace BtsS10SendTTTT.Tests
{
    /// <summary>
    /// FtpDirectory trong DB thường bắt đầu bằng '/'. Path.Combine vứt bỏ các đoạn đứng trước
    /// khi gặp đoạn bắt đầu bằng dấu phân cách, nên file sẽ bị ghi ra ngoài thư mục lưu trữ.
    /// Các test này khoá lại hành vi ghép đường dẫn.
    /// </summary>
    public class DuongDanLocalTests
    {
        private const string Goc = @"D:\BTS_GATEWAY_S10_TTTT\data_s10\";

        [Fact]
        public void Thu_muc_nhom_bat_dau_bang_gach_cheo_van_nam_trong_thu_muc_goc()
        {
            var duongDan = BtsGetway.GhepThuMucLocal(Goc, "/tttt_hoabinh", "2026/11/10");

            Assert.StartsWith(Goc, duongDan);
            Assert.Contains("tttt_hoabinh", duongDan);
        }

        [Fact]
        public void Thu_muc_nhom_bat_dau_bang_gach_nguoc_van_nam_trong_thu_muc_goc()
        {
            var duongDan = BtsGetway.GhepThuMucLocal(Goc, @"\tttt_hoabinh", "2026/11/10");

            Assert.StartsWith(Goc, duongDan);
        }

        [Fact]
        public void Thu_muc_nhom_khong_co_gach_dau_thi_ghep_binh_thuong()
        {
            var duongDan = BtsGetway.GhepThuMucLocal(Goc, "tttt_hoabinh", "2026/11/10");

            Assert.StartsWith(Goc, duongDan);
            Assert.Contains("tttt_hoabinh", duongDan);
        }

        [Fact]
        public void Thu_muc_ngay_bat_dau_bang_gach_cheo_van_nam_trong_thu_muc_goc()
        {
            var duongDan = BtsGetway.GhepThuMucLocal(Goc, "tttt", "/2026/11/10");

            Assert.StartsWith(Goc, duongDan);
        }

        [Fact]
        public void Khoang_trang_thua_duoc_cat_bo()
        {
            var duongDan = BtsGetway.GhepThuMucLocal(Goc, "  /tttt  ", " 2026/11/10 ");

            Assert.StartsWith(Goc, duongDan);
            Assert.DoesNotContain("  ", duongDan);
        }

        [Fact]
        public void Gia_tri_null_khong_lam_no()
        {
            var duongDan = BtsGetway.GhepThuMucLocal(Goc, null, null);

            Assert.StartsWith(Goc, duongDan);
        }
    }
}
