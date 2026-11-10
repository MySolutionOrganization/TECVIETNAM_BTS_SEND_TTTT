using Core.Logging;
using FluentFTP;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BtsGetwayService
{
    /// <summary>Thông tin kết nối FTP của một group.</summary>
    public class FtpTarget
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        /// <summary>Thư mục gốc trên server TTTT.</summary>
        public string RootDirectory { get; set; }

        public bool HopLe =>
            !string.IsNullOrWhiteSpace(Host)
            && !string.IsNullOrWhiteSpace(UserName)
            && !string.IsNullOrWhiteSpace(Password);
    }

    public interface IFtpUploader
    {
        /// <summary>
        /// Tải một file lên thư mục đích, tự tạo thư mục nếu chưa có.
        /// Trả về true nếu tải thành công.
        /// </summary>
        Task<bool> UploadAsync(FtpTarget target, string remoteDirectory, string fileName,
            string localFilePath, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Tải file lên FTP bằng FluentFTP.
    ///
    /// Thay cho WebClient/FtpWebRequest (đều obsolete từ .NET 6) của bản cũ. Bản cũ còn có hai lỗi:
    /// ép kiểu (FtpWebResponse)ex.Response mà không kiểm tra null -> NullReferenceException khi
    /// lỗi mạng, và nuốt mọi WebException khác khiến lỗi mạng bị hiểu nhầm thành "thư mục chưa có".
    /// </summary>
    public class FtpUploader : IFtpUploader
    {
        private readonly ILogger<FtpUploader> _logger;
        private readonly ILoggingService _loggingService;
        private readonly int _maxAttempts;

        public FtpUploader(ILogger<FtpUploader> logger, ILoggingService loggingService, int maxAttempts = 3)
        {
            _logger = logger;
            _loggingService = loggingService;
            _maxAttempts = maxAttempts > 0 ? maxAttempts : 3;
        }

        public async Task<bool> UploadAsync(FtpTarget target, string remoteDirectory, string fileName,
            string localFilePath, CancellationToken cancellationToken = default)
        {
            if (target == null || !target.HopLe)
            {
                _logger.LogWarning("Bo qua upload {FileName}: thieu thong tin ket noi FTP.", fileName);
                return false;
            }

            string remotePath = GhepDuongDan(remoteDirectory, fileName);

            for (int lan = 1; lan <= _maxAttempts; lan++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using (var client = new AsyncFtpClient(target.Host, target.UserName, target.Password, target.Port))
                    {
                        client.Config.RetryAttempts = 1;
                        await client.AutoConnect(cancellationToken);

                        var status = await client.UploadFile(
                            localFilePath,
                            remotePath,
                            FtpRemoteExists.Overwrite,
                            createRemoteDir: true,
                            token: cancellationToken);

                        if (status == FtpStatus.Success)
                        {
                            _logger.LogInformation("Da tai {FileName} len {RemotePath}.", fileName, remotePath);
                            return true;
                        }

                        _logger.LogWarning(
                            "Tai {FileName} len FTP khong thanh cong lan {Lan}/{Max}. Trang thai: {Status}.",
                            fileName, lan, _maxAttempts, status);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        "Loi khi tai {FileName} len FTP lan {Lan}/{Max}: {Loi}",
                        fileName, lan, _maxAttempts, ex.Message);
                    if (lan == _maxAttempts)
                        _loggingService.Error(ex);
                }

                if (lan < _maxAttempts)
                {
                    var cho = TimeSpan.FromSeconds(Math.Pow(2, lan));
                    await Task.Delay(cho, cancellationToken);
                }
            }

            _loggingService.Fatal($"Khong tai duoc file {fileName} len FTP {target.Host}{remotePath} sau {_maxAttempts} lan thu.");
            _logger.LogError("Khong tai duoc {FileName} len FTP sau {Max} lan thu.", fileName, _maxAttempts);
            return false;
        }

        private static string GhepDuongDan(string thuMuc, string tenFile)
        {
            if (string.IsNullOrEmpty(thuMuc))
                return "/" + tenFile;
            return thuMuc.TrimEnd('/') + "/" + tenFile;
        }

        /// <summary>
        /// Tách host và port từ giá trị FtpIp trong DB, vốn có thể là "1.2.3.4",
        /// "ftp://1.2.3.4" hoặc "ftp://1.2.3.4:21/".
        /// </summary>
        public static (string host, int port) TachHostVaPort(string ftpIp)
        {
            const int congMacDinh = 21;
            if (string.IsNullOrWhiteSpace(ftpIp))
                return ("", congMacDinh);

            string s = ftpIp.Trim();
            int viTriScheme = s.IndexOf("://", StringComparison.OrdinalIgnoreCase);
            if (viTriScheme >= 0)
                s = s.Substring(viTriScheme + 3);

            s = s.Trim('/');

            int viTriDauHai = s.LastIndexOf(':');
            if (viTriDauHai > 0 && int.TryParse(s.Substring(viTriDauHai + 1), out int port))
                return (s.Substring(0, viTriDauHai), port);

            return (s, congMacDinh);
        }
    }
}
