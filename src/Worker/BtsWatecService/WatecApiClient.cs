using Core.Logging;
using Core.Model;
using Core.Setting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BtsGetwayService
{
    /// <summary>
    /// Kết quả một lần gửi dữ liệu lên API Watec.
    /// </summary>
    public class WatecSendResult
    {
        public bool Success { get; set; }
        /// <summary>Mã HTTP trả về, hoặc tên lỗi nếu không gọi được tới server.</summary>
        public string StatusCode { get; set; }
        /// <summary>Nội dung body trả về, hoặc mô tả lỗi.</summary>
        public string Message { get; set; }
        /// <summary>Tổng số lần đã thử gửi (tính cả lần đầu).</summary>
        public int AttemptCount { get; set; }
    }

    public interface IWatecApiClient
    {
        Task<WatecSendResult> SendAsync(IReadOnlyList<WatecS10Model> data, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Gửi số liệu lên API Watec.
    ///
    /// Khác với <c>Core.Helper.ApiSend</c>: dùng lại HttpClient qua IHttpClientFactory thay vì
    /// tạo mới mỗi lần gọi (tránh cạn socket), có retry cho lỗi tạm thời, và log đầy đủ nội dung
    /// lỗi thay vì nuốt im.
    /// </summary>
    public class WatecApiClient : IWatecApiClient
    {
        public const string HttpClientName = "watec";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<WatecApiClient> _logger;
        private readonly ILoggingService _loggingService;
        private readonly AppApiWatecSetting _appSetting;

        public WatecApiClient(
            IHttpClientFactory httpClientFactory,
            IOptions<AppApiWatecSetting> option,
            ILogger<WatecApiClient> logger,
            ILoggingService loggingService)
        {
            _httpClientFactory = httpClientFactory;
            _appSetting = option.Value;
            _logger = logger;
            _loggingService = loggingService;
        }

        /// <summary>Số lần thử tối đa. Mặc định 3.</summary>
        private int MaxAttempts =>
            _appSetting.MaxSendAttempts > 0 ? _appSetting.MaxSendAttempts : 3;

        public async Task<WatecSendResult> SendAsync(IReadOnlyList<WatecS10Model> data, CancellationToken cancellationToken = default)
        {
            var result = new WatecSendResult { Success = false, StatusCode = "", Message = "" };
            if (data == null || data.Count == 0)
            {
                result.Success = true;
                result.Message = "Khong co du lieu de gui.";
                return result;
            }

            var json = JsonConvert.SerializeObject(data);

            for (int lan = 1; lan <= MaxAttempts; lan++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.AttemptCount = lan;

                bool coTheThuLai;
                try
                {
                    var client = _httpClientFactory.CreateClient(HttpClientName);
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (var response = await client.PostAsync(_appSetting.UrlPost, content, cancellationToken))
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        result.StatusCode = ((int)response.StatusCode).ToString();
                        result.Message = body;

                        if (response.IsSuccessStatusCode)
                        {
                            result.Success = true;
                            return result;
                        }

                        // 4xx: sai api key hoặc sai payload -> thử lại cũng vô ích.
                        coTheThuLai = (int)response.StatusCode >= 500;
                        _logger.LogWarning(
                            "Gui Watec that bai lan {Lan}/{Max}. HTTP {StatusCode}. Body: {Body}",
                            lan, MaxAttempts, (int)response.StatusCode, Rut(body));

                        if (!coTheThuLai)
                        {
                            _loggingService.Fatal(
                                $"Gui Watec that bai (khong retry). HTTP {(int)response.StatusCode}. Body: {Rut(body)}");
                            return result;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TaskCanceledException ex)
                {
                    // HttpClient timeout cũng ném TaskCanceledException.
                    coTheThuLai = true;
                    result.StatusCode = "TIMEOUT";
                    result.Message = ex.Message;
                    _logger.LogWarning("Gui Watec timeout lan {Lan}/{Max}: {Loi}", lan, MaxAttempts, ex.Message);
                }
                catch (HttpRequestException ex)
                {
                    coTheThuLai = true;
                    result.StatusCode = "NETWORK";
                    result.Message = ex.Message;
                    _logger.LogWarning("Gui Watec loi mang lan {Lan}/{Max}: {Loi}", lan, MaxAttempts, ex.Message);
                }
                catch (Exception ex)
                {
                    result.StatusCode = "ERROR";
                    result.Message = ex.Message;
                    _loggingService.Error(ex);
                    _logger.LogError(ex, "Gui Watec loi khong mong doi lan {Lan}/{Max}", lan, MaxAttempts);
                    return result;
                }

                if (lan < MaxAttempts)
                {
                    // Backoff 2s, 4s, 8s...
                    var cho = TimeSpan.FromSeconds(Math.Pow(2, lan));
                    _logger.LogInformation("Cho {GiaySecond}s roi thu gui lai Watec.", cho.TotalSeconds);
                    await Task.Delay(cho, cancellationToken);
                }
            }

            _loggingService.Fatal(
                $"Gui Watec that bai sau {result.AttemptCount} lan thu. {result.StatusCode}: {Rut(result.Message)}");
            return result;
        }

        /// <summary>Rút gọn body để không làm phình log.</summary>
        private static string Rut(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            return s.Length <= 500 ? s : s.Substring(0, 500) + "...";
        }
    }
}
