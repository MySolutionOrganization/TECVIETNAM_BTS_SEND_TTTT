using bts.udpgateway;
using BtsGetwayService;
using BtsGetwayService.Interface;
using BtsGetwayService.Service;
using Core;
using Core.Caching;
using Core.Logging;
using Core.MongoDb.Data.Interface;
using Core.MSSQL.Responsitory.Interface;
using Core.PushMessage;
using Core.Setting;
using ES_CapDien.AppCode;
using Infrastructure.Udp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Configuration;
using Telegram.Bot;

namespace UdpService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Serilog.Debugging.SelfLog.Enable(System.Console.Error);
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .AddSerilogElasticsearch("UdpService")
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<Connections>(hostContext.Configuration.GetSection("Connections"));
                    services.Configure<AppSettingUDP>(hostContext.Configuration.GetSection("AppSettingUDP"));
                    services.Configure<TelegramConfig>(hostContext.Configuration.GetSection("TelegramConfig"));
                    //services.Configure<WorkerRabbitmqConnection>(hostContext.Configuration.GetSection("WorkerRabbitmqConnection"));
                    //services.Configure<MasterRabbitmqConnection>(hostContext.Configuration.GetSection("MasterRabbitmqConnection"));
                    //services.AddSingleton<IMasterMessageQueueService, MasterRabbitmqService>();
                    //services.AddSingleton<IWorkerMessageQueueService, WorkerRabbitmqService>();
                    //services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(hostContext.Configuration.GetSection("RedisCacheConfig")["Configuration"]));
                    //services.AddStackExchangeRedisCache(option =>
                    //{
                    //    option.Configuration = hostContext.Configuration.GetSection("RedisCacheConfig")["Configuration"];
                    //    option.InstanceName = hostContext.Configuration.GetSection("RedisCacheConfig")["InstanceName"];
                    //});
                   // services.Configure<CacheSettings>(hostContext.Configuration.GetSection("CacheSettings"));
                    services.AddSingleton<IGroupData, GroupData>();
                    services.AddSingleton<IReportS10Data, ReportS10Data>();
                    services.AddSingleton<ISiteData, SiteData>();
                    services.AddSingleton<IReportDailyNhietDoData, ReportDailyNhietDoData>();
                    services.AddSingleton<IReportDailyDoAmData, ReportDailyDoAmData>();
                    services.AddSingleton<IReportDailyLuongMuaData, ReportDailyLuongMuaData>();
                    services.AddSingleton<IReportDailyApSuatData, ReportDailyApSuatData>();
                    services.AddSingleton<IReportDailyTocDoDongChayData, ReportDailyTocDoDongChayData>();
                    services.AddSingleton<IReportDailyMucNuocData, ReportDailyMucNuocData>();
                    services.AddSingleton<IReportDailyLuuLuongDongChayData, ReportDailyLuuLuongDongChayData>();
                    services.AddSingleton<IReportDailyBucXaMatTroiData, ReportDailyBucXaMatTroiData>();
                    services.AddSingleton<IReportDailyHuongGioData, ReportDailyHuongGioData>();
                    services.AddSingleton<IReportDailyTocDoGioData, ReportDailyTocDoGioData>();
                    services.AddSingleton<IRegisterSMSData, RegisterSMSData>();
                    services.AddSingleton<IObservationData, ObservationData>();
                    services.AddSingleton<ISMSServerData, SMSServerData>();
                    services.AddSingleton<IDataObservationMongoData, DataObservationMongoData>();
                    services.AddSingleton<IDataAlarmData, DataAlarmData>();
                    services.AddSingleton<ICommandData, CommandData>();

                    services.AddTransient<Helper>();
                    services.AddSingleton<IZipHelper, GZipHelper>();
                    services.AddHostedService<UdpService>();                    
                    services.AddLog4net();

                    services.AddTransient<IReportS10Service, ReportS10Service>();
                    services.AddTransient<IReportDailyService, ReportDailyService>();
                    services.AddTransient<IUdpService, Infrastructure.Udp.UdpService>();
                    services.AddTransient<IDataObservationService, DataObservationMongoService>();
                    services.AddTransient<IDataAlarmService, DataAlarmMongoService>();

                    //services.AddSingleton<IAsyncCacheService, RedisCacheService>();
                    // 2️⃣ Đăng ký ITelegramBotClient với BotToken từ config
                    services.AddSingleton<ITelegramBotClient>(sp =>
                    {
                        var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                        return new TelegramBotClient(config.BotToken);
                    });
                    services.AddSingleton<IPushMessageService, TelegramMessageService>();                    
                }).UseWindowsService();
    }
}
