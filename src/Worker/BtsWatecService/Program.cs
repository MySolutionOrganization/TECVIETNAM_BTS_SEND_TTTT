using bts.udpgateway;
using BtsGetwayService;
using Core;
using Core.Logging;
using Core.MSSQL.Responsitory.Interface;
using Core.PushMessage;
using Core.Setting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Net.Http.Headers;
using Telegram.Bot;

namespace BtsWatecService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .AddSerilogElasticsearch("BtsWatecService")
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<Connections>(hostContext.Configuration.GetSection("Connections"));
                    services.Configure<AppApiWatecSetting>(hostContext.Configuration.GetSection("AppApiWatecSetting"));
                    services.Configure<TelegramConfig>(hostContext.Configuration.GetSection("TelegramConfig"));
                    services.AddLog4net();

                    services.AddSingleton<IGroupData, GroupData>();
                    services.AddSingleton<IReportS10Data, ReportS10Data>();
                    services.AddSingleton<ISiteData, SiteData>();

                    services.AddSingleton<ITelegramBotClient>(sp =>
                    {
                        var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                        return new TelegramBotClient(config.BotToken);
                    });
                    services.AddSingleton<IPushMessageService, TelegramMessageService>();

                    // Dùng lại HttpClient qua factory thay vì new HttpClient() mỗi lần gọi (tránh cạn socket).
                    services.AddHttpClient(WatecApiClient.HttpClientName, (sp, client) =>
                    {
                        var cfg = sp.GetRequiredService<IOptions<AppApiWatecSetting>>().Value;
                        client.Timeout = TimeSpan.FromSeconds(30);
                        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        if (!string.IsNullOrEmpty(cfg.ApiKey))
                            client.DefaultRequestHeaders.Add("x-api-key", cfg.ApiKey);
                    });
                    services.AddSingleton<IWatecApiClient, WatecApiClient>();

                    // Bộ đếm bản tin bị mất của từng trạm - phải là singleton để giữ trạng thái giữa các chu kỳ.
                    services.AddSingleton<MissingDataTracker>();

                    services.AddTransient<Helper>();
                    services.AddTransient<RainDataCollector>();
                    services.AddTransient<BtsGetway>();
                    services.AddHostedService<Worker>();
                })
               .UseWindowsService();
    }
}
