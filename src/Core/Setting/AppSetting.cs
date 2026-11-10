using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Setting
{
    public class AppSetting
    {
        public string UrlDomainWebQuanTrac { get; set; }
        public string FolderLuuTruFile { get; set; }
        public int IsSendTTTT { get; set; }
    }
    public class AppApiWatecSetting
    {
        public string UrlPost { get; set; }
        public string ApiKey { get; set; }
        public int IsChooseGroup { get; set; }
        public int IsSendWatec { get; set; }
        public string ApiWatecKey { get; set; }
        public string ApiWatecUrl { get; set; }
        /// <summary>
        /// Số bản tin mất liên tiếp thì dừng phát về Watec và cảnh báo. Mặc định 3 nếu không cấu hình.
        /// Mất bản tin thứ 1..(MaxMissingMessages-1) vẫn phát với số liệu mưa = 0.
        /// </summary>
        public int MaxMissingMessages { get; set; }
        /// <summary>
        /// Số lần thử gửi tối đa khi API Watec lỗi tạm thời (mạng/timeout/5xx). Mặc định 3.
        /// </summary>
        public int MaxSendAttempts { get; set; }
    }
    public class AppSettingUDP
    {
        public int UdpPort { get; set; }        
        public bool ToDatabase { get; set; }        
        public bool IsUseRabbitMQ { get; set; }        
        public string UrlDomainWebQuanTrac { get; set; }        
    }
    public class JWT
    {
        public string Key { get; set; }
        public string Issuer { get; set; }
        public string Audience { get; set; }
        public int ExpireMinutes { get; set; }
    }
    public class JwtAccountConfig
    {
        public string Username { get; set; }
        public string Password { get; set; }        
    }
    public class CacheSettings
    {       
        public int CacheTime { get; set; }
    }
    public class TelegramConfig
    {
        public string BotToken { get; set; }
        public string ChatId { get; set; }
        public string ServerName { get; set; }
    }
    public class ElasticsearchLoggingSetting
    {
        public string NodeUris { get; set; }
        public string IndexPrefix { get; set; }
        public int BatchPostingLimit { get; set; }
        public int PeriodSeconds { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
