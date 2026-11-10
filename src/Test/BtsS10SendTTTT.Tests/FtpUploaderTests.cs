using BtsGetwayService;
using Xunit;

namespace BtsS10SendTTTT.Tests
{
    /// <summary>
    /// Giá trị FtpIp trong DB không có định dạng thống nhất, nên phần tách host/port
    /// phải chịu được mọi dạng đang có. Test thuần, không cần server FTP.
    /// </summary>
    public class FtpUploaderTests
    {
        [Theory]
        [InlineData("1.2.3.4", "1.2.3.4", 21)]
        [InlineData("ftp://1.2.3.4", "1.2.3.4", 21)]
        [InlineData("ftp://1.2.3.4/", "1.2.3.4", 21)]
        [InlineData("FTP://1.2.3.4/", "1.2.3.4", 21)]
        [InlineData("ftp://1.2.3.4:2121", "1.2.3.4", 2121)]
        [InlineData("ftp://1.2.3.4:2121/", "1.2.3.4", 2121)]
        [InlineData("  ftp://data.tttt.vn:990/  ", "data.tttt.vn", 990)]
        [InlineData("data.tttt.vn", "data.tttt.vn", 21)]
        public void Tach_dung_host_va_port(string input, string host, int port)
        {
            var (h, p) = FtpUploader.TachHostVaPort(input);

            Assert.Equal(host, h);
            Assert.Equal(port, p);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Gia_tri_rong_thi_tra_host_rong_va_port_mac_dinh(string input)
        {
            var (h, p) = FtpUploader.TachHostVaPort(input);

            Assert.Equal("", h);
            Assert.Equal(21, p);
        }

        [Fact]
        public void Thieu_thong_tin_dang_nhap_thi_target_khong_hop_le()
        {
            Assert.False(new FtpTarget { Host = "1.2.3.4", UserName = "", Password = "x" }.HopLe);
            Assert.False(new FtpTarget { Host = "", UserName = "u", Password = "x" }.HopLe);
            Assert.False(new FtpTarget { Host = "1.2.3.4", UserName = "u", Password = null }.HopLe);
            Assert.True(new FtpTarget { Host = "1.2.3.4", UserName = "u", Password = "x" }.HopLe);
        }
    }
}
