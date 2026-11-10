using bts.udpgateway;
using BtsGetwayService;
using Core.Logging;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BtsS10SendTTTT
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
           Host.CreateDefaultBuilder(args)
               .AddSerilogElasticsearch("BtsS10SendTTTT")
               .ConfigureServices((hostContext, services) =>
               {
                   services.Configure<Connections>(hostContext.Configuration.GetSection("Connections"));
                   services.Configure<AppSetting>(hostContext.Configuration.GetSection("AppSetting"));
                   services.AddLog4net();

                   services.AddSingleton<IGroupData, GroupData>();
                   services.AddSingleton<IReportS10Data, ReportS10Data>();
                   services.AddSingleton<ISiteData, SiteData>();

                   services.AddSingleton<IFtpUploader>(sp => new FtpUploader(
                       sp.GetRequiredService<ILogger<FtpUploader>>(),
                       sp.GetRequiredService<ILoggingService>()));

                   services.AddTransient<Helper>();
                   services.AddTransient<BtsGetway>();
                   services.AddHostedService<Worker>();
               })
               .UseWindowsService();
    }
}
