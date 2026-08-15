using Core.Setting;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Core.Logging
{
    /// <summary>
    /// Đăng ký log cho Serilog, ghi ra Console và đẩy lên Elasticsearch.
    /// Chạy song song với AddLog4net(), không thay thế log4net.
    /// </summary>
    public static class SerilogConfigure
    {
        private static string GetLocalIpAddress()
        {
            try
            {
                return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    ?.ToString() ?? Dns.GetHostName();
            }
            catch
            {
                return Dns.GetHostName();
            }
        }

        public static IHostBuilder AddSerilogElasticsearch(this IHostBuilder hostBuilder, string applicationName)
        {
            // writeToProviders: true - giữ lại các ILoggerProvider mà Host.CreateDefaultBuilder()/UseWindowsService()
            // đã đăng ký (vd. Windows Event Log), tránh mất log của các ILogger<T> hiện có khi chạy dưới dạng Windows Service.
            return hostBuilder.UseSerilog((context, loggerConfig) =>
            {
                var es = context.Configuration.GetSection("ElasticsearchLogging").Get<ElasticsearchLoggingSetting>();

                loggerConfig
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", applicationName)
                    .WriteTo.Console();

                if (es == null || string.IsNullOrWhiteSpace(es.NodeUris))
                {
                    return;
                }

                try
                {
                    var localIp = GetLocalIpAddress();
                    loggerConfig.WriteTo.Elasticsearch(new[] { new Uri(es.NodeUris) }, opts =>
                    {
                        opts.DataStream = new DataStreamName("logs", es.IndexPrefix, "default");
                        opts.BootstrapMethod = BootstrapMethod.Failure;
                        opts.TextFormatting.MapCustom = (document, logEvent) =>
                        {
                            if (document.Host != null)
                            {
                                document.Host.Hostname = localIp;
                            }
                            return document;
                        };
                        opts.ConfigureChannel = channelOpts =>
                        {
                            channelOpts.BufferOptions = new BufferOptions
                            {
                                OutboundBufferMaxSize = es.BatchPostingLimit,
                                OutboundBufferMaxLifetime = TimeSpan.FromSeconds(es.PeriodSeconds)
                            };
                        };
                    }, transport =>
                    {
                        transport.Authentication(new BasicAuthentication(es.Username, es.Password));
                        transport.RequestTimeout(TimeSpan.FromSeconds(5));
                    });
                }
                catch (Exception ex)
                {
                    // Elasticsearch shipping là best-effort observability, không phải chức năng cốt lõi.
                    // Nếu URI sai hoặc cluster không reachable lúc khởi động, vẫn tiếp tục chạy với Console sink,
                    // không được làm sập UdpService (luồng UDP mới là chính).
                    Serilog.Debugging.SelfLog.WriteLine($"Elasticsearch logging sink disabled: {ex.Message}");
                }
            }, writeToProviders: true);
        }
    }
}
