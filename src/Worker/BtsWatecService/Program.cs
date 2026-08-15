using bts.udpgateway;
using BtsGetwayService;
using Core;
using Core.Interfaces;
using Core.Logging;
using Core.MessageQueue;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
                    //services.AddHttpClient();
                    services.Configure<Connections>(hostContext.Configuration.GetSection("Connections"));
                    services.Configure<AppApiWatecSetting>(hostContext.Configuration.GetSection("AppApiWatecSetting"));
                    services.AddLog4net();
                    services.AddSingleton<IGroupData, GroupData>();
                    services.AddSingleton<IReportS10Data, ReportS10Data>();
                    services.AddSingleton<ISiteData, SiteData>();
                    
                    services.AddTransient<Helper>();
                    services.AddTransient<BtsGetway>();
                    services.AddSingleton<IZipHelper, GZipHelper>();
                    services.AddHostedService<Worker>();
                })
               .UseWindowsService();
    }
}
