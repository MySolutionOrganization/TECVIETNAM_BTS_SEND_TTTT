using bts.udpgateway.integration;
using Core.Logging;
using Core.PushMessage;
using Core.Setting;
using Infrastructure.Udp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UdpService
{
    public class UdpService : BackgroundService
    {
        private readonly ILogger<UdpService> _logger;

        private readonly IUdpService _udpService;
        static private UdpClient _udpClient = null;
        static private IPEndPoint _endpoint = null;
        private AppSettingUDP _appSettingUDP;
        private readonly ILoggingService _loggingService;
        private readonly IPushMessageService _pushMessageService;
        //public readonly IWorkerMessageQueueService _workerMessageQueueService;
        //public readonly IMasterMessageQueueService _masterMessageQueueService;
        public UdpService(ILogger<UdpService> logger,
            IUdpService udpService,
            ILoggingService loggingService,
            IPushMessageService pushMessageService,
            //IWorkerMessageQueueService workerMessageQueueService,
            //IMasterMessageQueueService masterMessageQueueService,
            IOptions<AppSettingUDP> options
            )
        {
            _logger = logger;
            _udpService = udpService;
            _appSettingUDP = options.Value;
            _loggingService = loggingService;
            _pushMessageService = pushMessageService;
            //_workerMessageQueueService = workerMessageQueueService;
            //_masterMessageQueueService = masterMessageQueueService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(1000, stoppingToken);
            }
        }
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            _pushMessageService.SendMessageAsync("UDP Service is started.");
            while (true)
            {
                try
                {
                    if (_udpClient == null)
                        _udpClient = new UdpClient(_appSettingUDP.UdpPort);
                    if (_endpoint == null)
                        _endpoint = new IPEndPoint(IPAddress.Any, _appSettingUDP.UdpPort);
                    // Get thong tin tra ve
                    byte[] bytes = _udpClient.Receive(ref _endpoint);
                    string package = Encoding.ASCII.GetString(bytes, 0, bytes.Length);
                    var subPackge = package;
                    string fpx = null;
                    if (package.StartsWith("EQID="))
                    {
                        var indexDevice = package.IndexOf(';');
                        fpx = subPackge.Substring(5, indexDevice - 5);
                    }

                    if (_appSettingUDP.ToDatabase)
                    {
                        var strs = package.Split(new string[] { "END;" }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string item in strs)
                        {
                            if (String.IsNullOrWhiteSpace(item))
                                continue;

                            string subMessage = item + "END;";
                            _logger.LogInformation("Data: " + subMessage);
                            if (_appSettingUDP.IsUseRabbitMQ)
                            {
                                //var message = new QueueMessage()
                                //{
                                //    Id = "DevicePostEvent"
                                //};
                                //message.Datas.Add("Message", subMessage);
                                //_workerMessageQueueService.PublishWorkerTask(message);
                            }
                            else
                            {
                                _loggingService.Info("message: " + subMessage);
                                ReturnInfo returnInfo = _udpService.Insert(subMessage).GetAwaiter().GetResult();
                            }


                            if (subMessage.Contains(";REQ;") && subMessage.Contains(";TIME;END;"))
                            {
                                var commandBack = string.Format("cmdC24={0:00}:{1:00}:{2:00}-{3:00}/{4:00}/{5:00}-{6:d};", DateTime.Now.Hour, DateTime.Now.Minute, DateTime.Now.Second, DateTime.Now.Day, DateTime.Now.Month, DateTime.Now.Year % 100, DateTime.Now.DayOfWeek + 1);
                                byte[] dgram = Encoding.ASCII.GetBytes(commandBack);
                                _udpClient.Send(dgram, dgram.Length, _endpoint);
                            }
                            if (subMessage.StartsWith("EQID="))
                            {
                                _udpService.SendCommand(subMessage, _endpoint, _udpClient);
                            }
                        }
                    }
                }
                catch (SocketException)
                {
                    if (_udpClient != null)
                    {
                        if (_udpClient.Client != null)
                        {
                            _udpClient.Client.Shutdown(SocketShutdown.Both);
                            _udpClient.Client.Close();
                        }
                        _udpClient.Close();
                        _udpClient = null;
                    }
                }
            }
        }
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogWarning("Worker stopping at: {0}", DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm"));
            return base.StopAsync(cancellationToken);
        }
    }
}
