using bts.udpgateway;
using BtsGetwayService;
using BtsGetwayService.Interface;
using BtsGetwayService.Service;
using CheckDeviceService;
using Core;
using Core.Interfaces;
using Core.Logging;
using Core.MessageQueue;
using Core.MongoDb.Data.Interface;
using Core.MSSQL.Responsitory.Interface;
using Core.PushMessage;
using Core.Setting;
using ES_CapDien.AppCode;
using Infrastructure.Udp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;

namespace CheckActiveDevice
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
           Host.CreateDefaultBuilder(args)
               .AddSerilogElasticsearch("CheckActiveDevice")
               .ConfigureServices((hostContext, services) =>
               {
                   services.Configure<Connections>(hostContext.Configuration.GetSection("Connections"));
                   services.Configure<AppSetting>(hostContext.Configuration.GetSection("AppSetting"));
                   services.Configure<AppSettingUDP>(hostContext.Configuration.GetSection("AppSettingUDP"));
                   services.Configure<WorkerRabbitmqConnection>(hostContext.Configuration.GetSection("WorkerRabbitmqConnection"));
                   services.Configure<MasterRabbitmqConnection>(hostContext.Configuration.GetSection("MasterRabbitmqConnection"));
                   services.Configure<TelegramConfig>(hostContext.Configuration.GetSection("TelegramConfig"));
                   services.AddSingleton<IMasterMessageQueueService, MasterRabbitmqService>();
                   services.AddSingleton<IWorkerMessageQueueService, WorkerRabbitmqService>();
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
                   services.AddHostedService<Worker>();
                   services.AddLog4net();

                   services.AddSingleton<IReportS10Service, ReportS10Service>();
                   services.AddTransient<IReportDailyService, ReportDailyService>();
                   services.AddTransient<IUdpService, Infrastructure.Udp.UdpService>();
                   services.AddTransient<IDataObservationService, DataObservationMongoService>();
                   services.AddTransient<IDataAlarmService, DataAlarmMongoService>();
                   services.AddSingleton<ITelegramBotClient>(sp =>
                   {
                       var config = sp.GetRequiredService<IOptions<TelegramConfig>>().Value;
                       return new TelegramBotClient(config.BotToken);
                   });
                   services.AddSingleton<IPushMessageService, TelegramMessageService>();
               }).UseWindowsService();
    }
}
