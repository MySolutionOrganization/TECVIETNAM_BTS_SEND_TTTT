using System;

namespace BtsGetwayService
{
    /// <summary>
    /// Cửa sổ thời gian lấy số liệu của một chu kỳ: [From, To).
    ///
    /// Dùng chung cho các service xuất báo cáo định kỳ (BtsWatecService, BtsS10SendTTTT...)
    /// để logic canh mốc chu kỳ chỉ tồn tại ở một nơi.
    /// </summary>
    public struct ReportTimeWindow
    {
        public DateTime From { get; }
        public DateTime To { get; }

        public ReportTimeWindow(DateTime from, DateTime to)
        {
            From = from;
            To = to;
        }

        /// <summary>
        /// Tính cửa sổ của chu kỳ LIỀN TRƯỚC - chu kỳ đã đóng hoàn toàn.
        ///
        /// Lý do không dùng chu kỳ hiện tại: service chạy ở mốc chu kỳ cộng vài phút, nếu query
        /// chu kỳ hiện tại thì chỉ bắt được vài phút đầu, trạm gửi trễ hơn sẽ bị coi là mất
        /// bản tin và bản tin thật đó vĩnh viễn không được xuất vì cửa sổ đã trôi qua.
        ///
        /// Ví dụ với chuKy = 10 phút, chạy lúc 10:11 -> trả về [10:00, 10:10).
        /// </summary>
        public static ReportTimeWindow ChuKyLienTruoc(DateTime now, TimeSpan chuKy)
        {
            if (chuKy <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(chuKy), "Chu ky phai lon hon 0.");

            DateTime to = RoundDown(now, chuKy);
            return new ReportTimeWindow(to - chuKy, to);
        }

        /// <summary>
        /// Làm tròn xuống mốc gần nhất chia hết cho <paramref name="d"/>.
        /// </summary>
        public static DateTime RoundDown(DateTime dt, TimeSpan d)
        {
            var delta = dt.Ticks % d.Ticks;
            return new DateTime(dt.Ticks - delta, dt.Kind);
        }

        /// <summary>
        /// Thời gian còn lại tới lần chạy tiếp theo: mốc chu kỳ cộng thời gian ân hạn
        /// để trạm kịp gửi bản tin về và DB kịp ghi.
        /// </summary>
        public static TimeSpan DelayToiMocTiepTheo(DateTime now, TimeSpan chuKy, TimeSpan anHan)
        {
            if (chuKy <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(chuKy), "Chu ky phai lon hon 0.");
            if (anHan < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(anHan), "An han khong duoc am.");

            // Mốc gần nhất vừa đóng, cộng ân hạn. Nếu còn ở tương lai thì chạy luôn mốc đó,
            // đừng nhảy sang chu kỳ sau kẻo bỏ lỡ một cửa sổ khi khởi động.
            DateTime mocTiepTheo = RoundDown(now, chuKy) + anHan;
            while (mocTiepTheo <= now)
                mocTiepTheo += chuKy;

            return mocTiepTheo - now;
        }

        public override string ToString()
        {
            return $"[{From:dd/MM/yyyy HH:mm:ss}, {To:dd/MM/yyyy HH:mm:ss})";
        }
    }
}
