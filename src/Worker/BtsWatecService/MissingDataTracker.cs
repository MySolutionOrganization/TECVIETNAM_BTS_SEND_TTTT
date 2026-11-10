using System;
using System.Collections.Concurrent;

namespace BtsGetwayService
{
    /// <summary>
    /// Kết quả sau khi ghi nhận một chu kỳ trạm KHÔNG có dữ liệu.
    /// </summary>
    public class MissingResult
    {
        /// <summary>Số bản tin bị mất liên tiếp tính đến chu kỳ hiện tại (bản tin này là thứ mấy).</summary>
        public int ConsecutiveMissing { get; set; }

        /// <summary>True: vẫn bù bản tin với số liệu mưa = 0. False: đã chạm ngưỡng, dừng phát.</summary>
        public bool ShouldSendZero { get; set; }

        /// <summary>True: đây là lần đầu chạm ngưỡng, cần bắn cảnh báo (chỉ đúng 1 lần cho mỗi đợt mất).</summary>
        public bool ShouldAlert { get; set; }
    }

    /// <summary>
    /// Kết quả sau khi ghi nhận một chu kỳ trạm CÓ dữ liệu.
    /// </summary>
    public class RecoveredResult
    {
        /// <summary>Số bản tin đã mất liên tiếp ngay trước khi có dữ liệu trở lại.</summary>
        public int PreviousMissing { get; set; }

        /// <summary>True: trạm này trước đó đã bị dừng phát và đã cảnh báo, cần báo khôi phục.</summary>
        public bool IsRecoveryFromAlert { get; set; }
    }

    /// <summary>
    /// Theo dõi số bản tin bị mất liên tiếp của từng trạm (theo DeviceId).
    ///
    /// Quy tắc:
    /// - Mất bản tin thứ 1..(MaxMissing-1): vẫn phát về Watec với số liệu mưa = 0
    ///   (KHÔNG lấy lại số liệu của bản tin trước).
    /// - Mất bản tin thứ MaxMissing trở đi: dừng phát cho trạm đó và cảnh báo đúng 1 lần.
    /// - Khi có dữ liệu trở lại: reset bộ đếm, nếu trước đó đã cảnh báo thì báo khôi phục.
    ///
    /// Lưu ý: trạng thái chỉ nằm trong bộ nhớ, service khởi động lại thì bộ đếm về 0.
    /// </summary>
    public class MissingDataTracker
    {
        private class SiteState
        {
            public int ConsecutiveMissing { get; set; }
            public bool AlertSent { get; set; }
        }

        private readonly ConcurrentDictionary<int, SiteState> _states = new ConcurrentDictionary<int, SiteState>();

        /// <summary>
        /// Ghi nhận chu kỳ hiện tại trạm không có dữ liệu.
        /// </summary>
        /// <param name="deviceId">DeviceId của trạm.</param>
        /// <param name="maxMissing">Số bản tin mất liên tiếp thì dừng phát (mặc định 3).</param>
        public MissingResult RegisterMissing(int deviceId, int maxMissing)
        {
            if (maxMissing < 1)
                maxMissing = 3;

            var state = _states.GetOrAdd(deviceId, _ => new SiteState());
            lock (state)
            {
                state.ConsecutiveMissing++;

                var result = new MissingResult
                {
                    ConsecutiveMissing = state.ConsecutiveMissing,
                    ShouldSendZero = state.ConsecutiveMissing < maxMissing,
                    ShouldAlert = false
                };

                if (!result.ShouldSendZero && !state.AlertSent)
                {
                    state.AlertSent = true;
                    result.ShouldAlert = true;
                }

                return result;
            }
        }

        /// <summary>
        /// Ghi nhận chu kỳ hiện tại trạm có dữ liệu, reset bộ đếm.
        /// </summary>
        public RecoveredResult RegisterReceived(int deviceId)
        {
            var state = _states.GetOrAdd(deviceId, _ => new SiteState());
            lock (state)
            {
                var result = new RecoveredResult
                {
                    PreviousMissing = state.ConsecutiveMissing,
                    IsRecoveryFromAlert = state.AlertSent
                };

                state.ConsecutiveMissing = 0;
                state.AlertSent = false;

                return result;
            }
        }
    }
}
