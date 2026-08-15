using bts.udpgateway;
using bts.udpgateway.integration;
using BtsGetwayService;
using BtsGetwayService.Interface;
using BtsGetwayService.MongoDb.Entity;
using Core.Logging;
using Core.MSSQL.Responsitory.Interface;
using Core.PushMessage;
using Core.Setting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Infrastructure.Udp
{
    public class UdpService : IUdpService
    {
        public readonly IGroupData _groupData;
        public readonly ISiteData _siteData;
        public readonly AppSettingUDP _appSetting;
        private readonly ILoggingService _loggingService;
        public readonly ICommandData _commandData;

        public readonly IDataObservationService _dataObservationMongoService;
        public readonly IDataAlarmService _dataAlarmService;
        public readonly IRegisterSMSData _registerSMSData;
        public readonly IObservationData _observationData;
        public readonly ISMSServerData _sMSServerData;
        public readonly IReportS10Service _reportS10Service;
        public readonly IReportDailyService _reportDailyService;

        private readonly ILogger<UdpService> _logger;
        Helper helperUlti = new Helper();

        private readonly IPushMessageService _pushMessageService;
        public UdpService(IOptions<AppSettingUDP> option,
            IGroupData groupData,
            ISiteData siteData,
            ICommandData commandData,
            ISMSServerData sMSServerData,
            IObservationData observationData,
            IRegisterSMSData registerSMSData,
            ILoggingService loggingService,
            IReportS10Service reportS10Service,
            IReportDailyService reportDailyService,
            IDataObservationService dataObservationMongoService,
            IDataAlarmService dataAlarmService,
            ILogger<UdpService> logger,
            IPushMessageService pushMessageService
           )
        {
            _appSetting = option.Value;
            _groupData = groupData;
            _siteData = siteData;
            _commandData = commandData;
            _sMSServerData = sMSServerData;
            _observationData = observationData;
            _registerSMSData = registerSMSData;
            _loggingService = loggingService;
            _dataObservationMongoService = dataObservationMongoService;
            _dataAlarmService = dataAlarmService;
            _reportS10Service = reportS10Service;
            _reportDailyService = reportDailyService;
            _pushMessageService = pushMessageService;
            _logger = logger;
        }
        public async Task<ReturnInfo> Insert(string msg_content)
        {
            try
            {
                if (msg_content.Contains("ALM"))
                {
                    DataAlarm dataAlarmMongo = InitAlarm(msg_content);
                    _dataAlarmService.Insert(dataAlarmMongo);
                    RegisterSMS registerSMS = _registerSMSData.GetRegisterSMS(dataAlarmMongo.Device_Id).FirstOrDefault();
                    if (registerSMS != null)
                    {
                        SMSServer sMSServer = _sMSServerData.FindByKey(registerSMS.SMSServerId);
                        List<string> listCode = registerSMS.CodeObservation.Split(',').ToList();
                        string content = "", dsSDT = "";
                        Site site = await _siteData.GetSiteByDeviceId(dataAlarmMongo.Device_Id);
                        if (site != null)
                        {
                            content = content + RejectMarks(site.Name) + "," + dataAlarmMongo.DateCreate.ToString("HH:mm:ss");
                            foreach (var item in listCode)
                            {
                                Observation observation = _observationData.GetByCode(item.Trim());
                                if (observation != null)
                                {
                                    if (dataAlarmMongo.AMATI.Contains("0-1") || dataAlarmMongo.AMATI.Contains("1-1"))
                                    {
                                        content = content + "#" + observation.Name;
                                    }
                                    else if (dataAlarmMongo.AMAFR.Contains("0-1") || dataAlarmMongo.AMAFR.Contains("1-1"))
                                    {
                                        content = content + "#" + observation.Name;
                                    }
                                }

                            }
                            dsSDT = registerSMS.DanhSachSDT;
                            if (dsSDT != "")
                            {
                                SendSMS(sMSServer.AddressIP.Trim(), sMSServer.Port, dsSDT, content);
                            }
                        }

                    }

                }
                else if (msg_content.Contains("SEQ"))
                {
                    _dataObservationMongoService.Insert(Init(msg_content));
                }
                else if (msg_content.Contains("S10"))
                {
                    _reportS10Service.InsertS10(_reportS10Service.InitS10(msg_content));
                }
                else if (msg_content.Contains("RP"))
                {
                    _reportDailyService.ReportDaily(msg_content);
                }
                return new ReturnInfo(ReturnCode.Success, (string)null, (object)null);
            }
            catch (Exception ex)
            {
                _loggingService.Error(ex);
                return new ReturnInfo(ReturnCode.Success, (string)null, (object)null);
            }
        }
        public ReturnInfo SendCommand(string message, IPEndPoint endPoint, UdpClient udpClient)
        {
            try
            {
                string[] strArray = message.Split(';');
                string s1 = strArray[0].Split('=')[1];
                int deviceId = int.Parse(s1);
                var command = _commandData.GetAllCommandByDeviceId(deviceId).FirstOrDefault();
                if (command != null)
                {
                    string commandStr = command.Command_Content.Trim();
                    byte[] dgram = Encoding.ASCII.GetBytes(commandStr);
                    int result = udpClient.Send(dgram, dgram.Length, endPoint);
                    command.Status = true;
                    command.UpdateDay = DateTime.Now;
                    if (result > 0)
                    {
                        _commandData.Update(command);
                    }
                }
            }
            catch (Exception ex)
            {
                _loggingService.Error(ex);
            }

            return new ReturnInfo(ReturnCode.Success, (string)null, (object)null);
        }
        #region SQL and Mongo               

        private Data Init(string message)
        {
            Data dataMongo = new Data();
            dataMongo.Content = message;
            IDictionary<string, string> dictionary1 = (IDictionary<string, string>)new Dictionary<string, string>();
            Dictionary<string, string> dictionary2 = new Dictionary<string, string>()
              {
                {
                  "EQID",
                  "ID thiet bi"
                },
                {
                  "SEQ",
                  "Lưu vào DB"
                },
                {
                  "DATE",
                  "Thời gian"
                },
                {
                  "BTI",
                  "Nhiet do trong phong"
                },
                {
                  "BHU",
                  "Do am moi truong"
                },
                {
                  "BTO",
                  "Nhiet do ben ngoai"
                },
                {
                  "BDR",
                  "Dot nhap"
                },
                {
                  "BFL",
                  "Ngap nuoc "
                },
                {
                  "BPR",
                  "Buc xa mat troi"
                },
                {
                  "BFR",
                  "Chay/Khoi"
                },
                {
                  "BPS",
                  "Nguon cap cho sensor"
                },
                {
                  "BAV",
                  "Dien ap AC"
                },
                {
                  "BAP",
                  "Tan so"
                },
                {
                  "BAC",
                  "Dong dien AC"
                },
                {
                  "BAF",
                  "Cosa"
                },
                {
                  "BWS",
                  "Tốc độ gió"
                },
                {
                  "BV1",
                  "Dien ap acquy 1"
                },
                {
                  "BC1",
                  "Dong dien acquy 1"
                },
                {
                  "BT1",
                  "Nhiet do acquy 1"
                },
                {
                  "BV2",
                  "Dien ap acquy 2"
                },
                {
                  "BC2",
                  "Dong dien acquy 2"
                },
                {
                  "BT2",
                  "Nhiet do acquy 2"
                },
                {
                  "BSE",
                  "Dien dang su dung"
                },
                {
                  "BA1",
                  "Dieu hoa 1"
                },
                {
                  "BB1",
                  "Dong dieu hoa 1"
                },
                {
                  "BA2",
                  "Dieu hoa 2"
                },
                {
                  "BB2",
                  "Dong dieu hoa 2"
                },
                {
                  "BA3",
                  "Dieu hoa 3"
                },
                {
                  "BB3",
                  "Dong dieu hoa 3"
                },
                {
                  "BA4",
                  "Dieu hoa 4"
                },
                {
                  "BB4",
                  "Dong dieu hoa 4"
                },
                {
                  "BFA",
                  "Quat AC"
                },
                {
                  "BFD",
                  "Quat DC"
                },
                {
                  "BPW",
                  "Cong suat dien AC"
                },
                 {
                  "BVC",
                  "Tốc độ dòng chảy"
                },
                {
                  "BTS",
                  "Nhiệt độ muối"
                },
                {
                  "BEC",
                  "Độ dẫn điện"
                },
                {
                  "BEV",
                  "Độ bốc hơi"
                }
              };
            string[] strArray = message.Split(';');
            string s1 = strArray[0].Split('=')[1];
            dataMongo.Device_Id = int.Parse(s1);
            string str1 = strArray[1];
            string str2 = strArray[2];
            dataMongo.DateCreate = DateTime.ParseExact(str2, "HH:mm:ss-dd/MM/yy",
                                       System.Globalization.CultureInfo.InvariantCulture); ;
            dictionary1.Add(new KeyValuePair<string, string>("EQID", s1));
            dictionary1.Add(new KeyValuePair<string, string>("SEQ", "1"));
            dictionary1.Add(new KeyValuePair<string, string>("DATE", str2));
            for (int index = 3; index < strArray.Length - 2; ++index)
            {
                strArray[index] = strArray[index].Trim();
                string str3 = strArray[index].Substring(0, 3);
                string s2 = double.Parse(strArray[index].Substring(3)).ToString();
                switch (str3)
                {
                    case "BA1":
                        dataMongo.BA1 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BPR":
                        dataMongo.BPR = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BA2":
                        dataMongo.BA2 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BA3":
                        dataMongo.BA3 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BA4":
                        dataMongo.BA4 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BAC":
                        dataMongo.BAC = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BAF":
                        dataMongo.BAF = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BAP":
                        dataMongo.BAP = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BAV":
                        dataMongo.BAV = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BB1":
                        dataMongo.BB1 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BB2":
                        dataMongo.BB2 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BB3":
                        dataMongo.BB3 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BC1":
                        dataMongo.BC1 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BC2":
                        dataMongo.BC2 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BDR":
                        dataMongo.BDR = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BFA":
                        dataMongo.BFA = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BFD":
                        dataMongo.BFD = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BFL":
                        dataMongo.BFL = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BFR":
                        dataMongo.BFR = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BHU":
                        dataMongo.BHU = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BPS":
                        dataMongo.BPS = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BPW":
                        dataMongo.BPW = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BSE":
                        dataMongo.BSE = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BT1":
                        dataMongo.BT1 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BT2":
                        dataMongo.BT2 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BTI":
                        dataMongo.BTI = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BTO":
                        dataMongo.BTO = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BV1":
                        dataMongo.BV1 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BV2":
                        dataMongo.BV2 = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BWS":
                        dataMongo.BWS = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BVC":
                        dataMongo.BVC = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BTS":
                        dataMongo.BTS = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BEC":
                        dataMongo.BEC = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                    case "BEV":
                        dataMongo.BEV = s2 == null ? 2.0 : double.Parse(s2, (IFormatProvider)CultureInfo.InvariantCulture);
                        break;
                }
            }
            return dataMongo;
        }

        private DataAlarm InitAlarm(string message)
        {
            DataAlarm dataAlarmMongo = new DataAlarm();
            dataAlarmMongo.Content = message;
            Dictionary<string, string> dictionary1 = new Dictionary<string, string>();
            Dictionary<string, string> dictionary2 = new Dictionary<string, string>()
      {
        {
          "AMATI",
          "Nhiet do phong cao"
        },
        {
          "AMIHU",
          "Do am cao"
        },
        {
          "AMADR",
          "Co dot nhap"
        },
        {
          "AMAFL",
          "Co ngap nuoc"
        },
        {
          "AMAFR",
          "Co chay khoi"
        },
        {
          "AMIPS",
          "Chap nguon sensor"
        },
        {
          "AMIAL",
          "Dien AC thap"
        },
        {
          "AMIAH",
          "Dien AC cao"
        },
        {
          "AMIAP",
          "Tan so khong on dinh"
        },
        {
          "AMIAC",
          "Mat dien AC"
        },
        {
          "AMIGN",
          "Chay may phat"
        },
        {
          "AMIAR",
          "Dieu hoa gap su co"
        },
        {
          "AMIL1",
          "Dien ap acquy 1 thap"
        },
        {
          "AMIH1",
          "Dien ap acquy 1 cao"
        },
        {
          "AMIT1",
          "Nhiet do acquy 1 cao"
        },
        {
          "AMIL2",
          "Dien ap acquy 2 thap"
        },
        {
          "AMIH2",
          "Dien ap acquy 2 cao"
        },
        {
          "AMIT2",
          "Nhiet do acquy 2 cao"
        }
      };
            string[] strArray1 = message.Split(';');
            string s = strArray1[0].Split('=')[1];
            dataAlarmMongo.Device_Id = int.Parse(s);
            dataAlarmMongo.DateCreate = DateTime.Now;
            for (int index1 = 3; index1 < strArray1.Length - 2; ++index1)
            {
                strArray1[index1] = strArray1[index1].Trim();
                int[] numArray = new int[2] { 5, 3 };
                if (strArray1[index1].Length > 6)
                {
                    string[] strArray2 = strArray1[index1].Split('|');
                    for (int index2 = 0; index2 < strArray2.Length; ++index2)
                    {
                        string str1 = strArray2[index2].Substring(0, numArray[index2]);
                        string str2 = strArray2[index2].Substring(numArray[index2]);
                        switch (str1)
                        {
                            case "AMADR":
                                dataAlarmMongo.AMADR = str2;
                                break;
                            case "AMAFL":
                                dataAlarmMongo.AMAFL = str2;
                                break;
                            case "AMAFR":
                                dataAlarmMongo.AMAFR = str2;
                                break;
                            case "AMATI":
                                dataAlarmMongo.AMATI = str2;
                                break;
                            case "AMIAC":
                                dataAlarmMongo.AMIAC = str2;
                                break;
                            case "AMIAH":
                                dataAlarmMongo.AMIAH = str2;
                                break;
                            case "AMIAL":
                                dataAlarmMongo.AMIAL = str2;
                                break;
                            case "AMIAP":
                                dataAlarmMongo.AMIAP = str2;
                                break;
                            case "AMIAR":
                                dataAlarmMongo.AMIAR = str2;
                                break;
                            case "AMIGN":
                                dataAlarmMongo.AMIGN = str2;
                                break;
                            case "AMIH1":
                                dataAlarmMongo.AMIH1 = str2;
                                break;
                            case "AMIH2":
                                dataAlarmMongo.AMIH2 = str2;
                                break;
                            case "AMIHU":
                                dataAlarmMongo.AMIHU = str2;
                                break;
                            case "AMIL1":
                                dataAlarmMongo.AMIL1 = str2;
                                break;
                            case "AMIL2":
                                dataAlarmMongo.AMIL2 = str2;
                                break;
                            case "AMIPS":
                                dataAlarmMongo.AMIPS = str2;
                                break;
                            case "AMIT1":
                                dataAlarmMongo.AMIT1 = str2;
                                break;
                            case "AMIT2":
                                dataAlarmMongo.AMIT2 = str2;
                                break;
                        }
                    }
                }
                else
                {
                    string str1 = strArray1[index1].Substring(0, numArray[0]);
                    string str2 = strArray1[index1].Substring(numArray[0]);
                    switch (str1)
                    {
                        case "AMADR":
                            dataAlarmMongo.AMADR = str2;
                            continue;
                        case "AMAFL":
                            dataAlarmMongo.AMAFL = str2;
                            continue;
                        case "AMAFR":
                            dataAlarmMongo.AMAFR = str2;
                            continue;
                        case "AMATI":
                            dataAlarmMongo.AMATI = str2;
                            continue;
                        case "AMIAC":
                            dataAlarmMongo.AMIAC = str2;
                            continue;
                        case "AMIAH":
                            dataAlarmMongo.AMIAH = str2;
                            continue;
                        case "AMIAL":
                            dataAlarmMongo.AMIAL = str2;
                            continue;
                        case "AMIAP":
                            dataAlarmMongo.AMIAP = str2;
                            continue;
                        case "AMIAR":
                            dataAlarmMongo.AMIAR = str2;
                            continue;
                        case "AMIGN":
                            dataAlarmMongo.AMIGN = str2;
                            continue;
                        case "AMIH1":
                            dataAlarmMongo.AMIH1 = str2;
                            continue;
                        case "AMIH2":
                            dataAlarmMongo.AMIH2 = str2;
                            continue;
                        case "AMIHU":
                            dataAlarmMongo.AMIHU = str2;
                            continue;
                        case "AMIL1":
                            dataAlarmMongo.AMIL1 = str2;
                            continue;
                        case "AMIL2":
                            dataAlarmMongo.AMIL2 = str2;
                            continue;
                        case "AMIPS":
                            dataAlarmMongo.AMIPS = str2;
                            continue;
                        case "AMIT1":
                            dataAlarmMongo.AMIT1 = str2;
                            continue;
                        case "AMIT2":
                            dataAlarmMongo.AMIT2 = str2;
                            continue;
                        default:
                            continue;
                    }
                }
            }
            return dataAlarmMongo;
        }
        private void SendSMS(string SERVER_IP, int PORT_NO, string sdt, string message)
        {
            //---data to send to the server---
            string textToSend = $"***\r\n{sdt}\r\n{message}\r\n";
            //---create a TCPClient object at the IP and port no.---
            TcpClient client = new TcpClient(SERVER_IP, PORT_NO);
            NetworkStream nwStream = client.GetStream();
            byte[] bytesToSend = ASCIIEncoding.ASCII.GetBytes(textToSend);

            //---send the text---           
            nwStream.Write(bytesToSend, 0, bytesToSend.Length);
            client.Close();
        }
        #endregion
        #region Chuyển đổi chữ có dấu thành không dấu
        private string RejectMarks(string text)
        {
            string[] pattern = new string[7];
            char[] replaceChar = new char[14];

            // Khởi tạo giá trị thay thế

            replaceChar[0] = 'a';
            replaceChar[1] = 'd';
            replaceChar[2] = 'e';
            replaceChar[3] = 'i';
            replaceChar[4] = 'o';
            replaceChar[5] = 'u';
            replaceChar[6] = 'y';
            replaceChar[7] = 'A';
            replaceChar[8] = 'D';
            replaceChar[9] = 'E';
            replaceChar[10] = 'I';
            replaceChar[11] = 'O';
            replaceChar[12] = 'U';
            replaceChar[13] = 'Y';

            //Mẫu cần thay thế tương ứng

            pattern[0] = "(á|à|ả|ã|ạ|ă|ắ|ằ|ẳ|ẵ|ặ|â|ấ|ầ|ẩ|ẫ|ậ)"; //letter a
            pattern[1] = "đ"; //letter d
            pattern[2] = "(é|è|ẻ|ẽ|ẹ|ê|ế|ề|ể|ễ|ệ)"; //letter e
            pattern[3] = "(í|ì|ỉ|ĩ|ị)"; //letter i
            pattern[4] = "(ó|ò|ỏ|õ|ọ|ô|ố|ồ|ổ|ỗ|ộ|ơ|ớ|ờ|ở|ỡ|ợ)"; //letter o
            pattern[5] = "(ú|ù|ủ|ũ|ụ|ư|ứ|ừ|ử|ữ|ự)"; //letter u
            pattern[6] = "(ý|ỳ|ỷ|ỹ|ỵ)"; //letter y

            for (int i = 0; i < pattern.Length; i++)
            {
                MatchCollection matchs = Regex.Matches(text, pattern[i], RegexOptions.IgnoreCase);
                foreach (Match m in matchs)
                {
                    if (i == 0)
                    {
                        text = text.Replace(m.Value[0], 'a');
                    }
                    else if (i == 1)
                    {
                        text = text.Replace(m.Value[0], 'd');
                    }
                    else if (i == 2)
                    {
                        text = text.Replace(m.Value[0], 'e');
                    }
                    else if (i == 3)
                    {
                        text = text.Replace(m.Value[0], 'i');
                    }
                    else if (i == 4)
                    {
                        text = text.Replace(m.Value[0], 'o');
                    }
                    else if (i == 5)
                    {
                        text = text.Replace(m.Value[0], 'u');
                    }
                    else if (i == 6)
                    {
                        text = text.Replace(m.Value[0], 'y');
                    }
                }
            }
            return text;
        }
        #endregion
        public async Task<bool> CheckDevice()
        {
            List<Site> lst = new List<Site>();
            lst = _siteData.FindAll().ToList();
            foreach (var item in lst)
            {
                try
                {
                    var start = DateTime.Now.AddMinutes(-20); //2017-04-05 15:21:23.234
                    var end = DateTime.Now;//2017-04-04 15:21:23.234                                       
                    List<Data> searchResult2 = (await _dataObservationMongoService.GetDatayDeviceId(start, end, item.DeviceId.Value)).ToList();
                    if (searchResult2.Count() == 0)
                    {
                        item.IsActive = false;
                        _siteData.Update(item);
                    }
                    else
                    {
                        item.IsActive = true;
                        _siteData.Update(item);
                    }
                }
                catch (Exception)
                {
                    //XtraLog.Write($"[GET:RESULT] Update Active Device :{ex.Message}, {ex.StackTrace}", LogLevel.INFO, "Integrator.Insert");
                }

            }

            bool result = false;

            return result;
        }
        public async Task CheckDeviceS10()
        {
            try
            {
                List<Site> lst = new List<Site>();
                lst = _siteData.FindAll().ToList();
                List<int> lstDeviceId = new List<int>();
                List<int> lstDeviceIdUpdateActive = new List<int>();
                List<string> mesagePushDisable = new List<string>();
                List<string> mesagePushActive = new List<string>();
                int indexDisable = 1, indexActive = 1;
                var listMessage = new List<string>();
                foreach (var item in lst)
                {
                    try
                    {
                        var start = DateTime.Now.AddMinutes(-20); //2017-04-05 15:21:23.234
                        var end = DateTime.Now;//2017-04-04 15:21:23.234                                                                              
                        var lstData = _reportS10Service.GetByTime(start, end, item.DeviceId.Value, null, null, null);
                        if (lstData.Any())
                        {
                            lstDeviceIdUpdateActive.Add(item.DeviceId.Value);
                            if (item.IsAlarm == true)
                            {
                                listMessage.Add($"{indexActive}. 🟢 Thiết bị {item.DeviceId} - {item.Name} đã online trở lại.");
                                indexActive++;
                            }
                        }
                        else
                        {
                            lstDeviceId.Add(item.DeviceId.Value);
                            if (item.IsAlarm != true)
                            {
                                listMessage.Add($"{indexDisable}. 🔴 Thiết bị {item.DeviceId} - {item.Name} không có dữ liệu trong 20 phút qua.");
                                indexDisable++;
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                _siteData.UpdateStatusDisable(lstDeviceId);
                _siteData.UpdateStatusActive(lstDeviceIdUpdateActive);                             
                if (listMessage.Any())
                {
                    await _pushMessageService.SendMessageAsync(string.Join("\n", listMessage));
                }
                _logger.LogInformation("update disable: " + lstDeviceId.Count() + "\n update active: " + lstDeviceIdUpdateActive.Count());
                await Core.Helper.ApiSend.Call_PostDataAsync(null, _appSetting.UrlDomainWebQuanTrac, "Administrator/SuperAdmin/CacheManagerRemoveAll", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi cập nhật");
            }

        }
    }
}
