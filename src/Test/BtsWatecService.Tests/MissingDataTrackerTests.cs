using BtsGetwayService;
using Xunit;

namespace BtsWatecService.Tests
{
    /// <summary>
    /// Kiểm tra quy tắc: mất bản tin thứ 1-2 thì vẫn phát với val = 0,
    /// mất bản tin thứ 3 liên tiếp thì dừng phát và cảnh báo đúng 1 lần.
    /// Test thuần, không cần DB.
    /// </summary>
    public class MissingDataTrackerTests
    {
        private const int DeviceId = 12345;
        private const int MaxMissing = 3;

        [Fact]
        public void Mat_ban_tin_thu_1_va_2_thi_van_bu_so_lieu_0()
        {
            var tracker = new MissingDataTracker();

            var lan1 = tracker.RegisterMissing(DeviceId, MaxMissing);
            Assert.Equal(1, lan1.ConsecutiveMissing);
            Assert.True(lan1.ShouldSendZero);
            Assert.False(lan1.ShouldAlert);

            var lan2 = tracker.RegisterMissing(DeviceId, MaxMissing);
            Assert.Equal(2, lan2.ConsecutiveMissing);
            Assert.True(lan2.ShouldSendZero);
            Assert.False(lan2.ShouldAlert);
        }

        [Fact]
        public void Mat_ban_tin_thu_3_thi_dung_phat_va_canh_bao()
        {
            var tracker = new MissingDataTracker();
            tracker.RegisterMissing(DeviceId, MaxMissing);
            tracker.RegisterMissing(DeviceId, MaxMissing);

            var lan3 = tracker.RegisterMissing(DeviceId, MaxMissing);
            Assert.Equal(3, lan3.ConsecutiveMissing);
            Assert.False(lan3.ShouldSendZero);
            Assert.True(lan3.ShouldAlert);
        }

        [Fact]
        public void Da_canh_bao_roi_thi_cac_chu_ky_sau_khong_canh_bao_lai()
        {
            var tracker = new MissingDataTracker();
            for (int i = 0; i < 3; i++)
                tracker.RegisterMissing(DeviceId, MaxMissing);

            var lan4 = tracker.RegisterMissing(DeviceId, MaxMissing);
            Assert.False(lan4.ShouldSendZero);
            Assert.False(lan4.ShouldAlert);

            var lan5 = tracker.RegisterMissing(DeviceId, MaxMissing);
            Assert.False(lan5.ShouldAlert);
        }

        [Fact]
        public void Co_du_lieu_tro_lai_thi_reset_bo_dem()
        {
            var tracker = new MissingDataTracker();
            tracker.RegisterMissing(DeviceId, MaxMissing);
            tracker.RegisterMissing(DeviceId, MaxMissing);

            tracker.RegisterReceived(DeviceId);

            var sauKhiReset = tracker.RegisterMissing(DeviceId, MaxMissing);
            Assert.Equal(1, sauKhiReset.ConsecutiveMissing);
            Assert.True(sauKhiReset.ShouldSendZero);
        }

        [Fact]
        public void Khoi_phuc_sau_khi_da_canh_bao_thi_bao_khoi_phuc()
        {
            var tracker = new MissingDataTracker();
            for (int i = 0; i < 3; i++)
                tracker.RegisterMissing(DeviceId, MaxMissing);

            var khoiPhuc = tracker.RegisterReceived(DeviceId);
            Assert.True(khoiPhuc.IsRecoveryFromAlert);
            Assert.Equal(3, khoiPhuc.PreviousMissing);
        }

        [Fact]
        public void Khoi_phuc_khi_chua_canh_bao_thi_khong_bao_khoi_phuc()
        {
            var tracker = new MissingDataTracker();
            tracker.RegisterMissing(DeviceId, MaxMissing);

            var khoiPhuc = tracker.RegisterReceived(DeviceId);
            Assert.False(khoiPhuc.IsRecoveryFromAlert);
            Assert.Equal(1, khoiPhuc.PreviousMissing);
        }

        [Fact]
        public void Mat_lai_sau_khi_khoi_phuc_thi_canh_bao_lan_nua()
        {
            var tracker = new MissingDataTracker();
            for (int i = 0; i < 3; i++)
                tracker.RegisterMissing(DeviceId, MaxMissing);
            tracker.RegisterReceived(DeviceId);

            tracker.RegisterMissing(DeviceId, MaxMissing);
            tracker.RegisterMissing(DeviceId, MaxMissing);
            var lan3 = tracker.RegisterMissing(DeviceId, MaxMissing);

            Assert.True(lan3.ShouldAlert);
        }

        [Fact]
        public void Bo_dem_cua_cac_tram_doc_lap_voi_nhau()
        {
            var tracker = new MissingDataTracker();
            tracker.RegisterMissing(1, MaxMissing);
            tracker.RegisterMissing(1, MaxMissing);
            tracker.RegisterMissing(1, MaxMissing);

            var tramKhac = tracker.RegisterMissing(2, MaxMissing);
            Assert.Equal(1, tramKhac.ConsecutiveMissing);
            Assert.True(tramKhac.ShouldSendZero);
        }

        [Fact]
        public void MaxMissing_khong_hop_le_thi_dung_mac_dinh_3()
        {
            var tracker = new MissingDataTracker();
            Assert.True(tracker.RegisterMissing(DeviceId, 0).ShouldSendZero);
            Assert.True(tracker.RegisterMissing(DeviceId, 0).ShouldSendZero);
            Assert.False(tracker.RegisterMissing(DeviceId, 0).ShouldSendZero);
        }
    }
}
