using Core.Model.Report.ReportDay;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BtsGetwayService
{
    public class Helper
    {
        public string CreateTitle(string strTitle)
        {
            string title = strTitle;
            if (String.IsNullOrEmpty(title)) return "";
            title = RejectMarks(title);
            // remove entities
            title = Regex.Replace(title, @"&\w+;", "");
            // remove anything that is not letters, numbers, dash, or space
            title = Regex.Replace(title, @"[^A-Za-z0-9\-\s]", "");
            // remove any leading or trailing spaces left over
            title = title.Trim();
            // replace spaces with single dash
            title = Regex.Replace(title, @"\s+", "-");
            // if we end up with multiple dashes, collapse to single dash            
            title = Regex.Replace(title, @"\-{2,}", "-");
            // make it all lower case
            title = title.ToLower();
            // if it's too long, clip it
            if (title.Length > 80)
                title = title.Substring(0, 79);
            // remove trailing dash, if there is one
            if (title.EndsWith("-"))
                title = title.Substring(0, title.Length - 1);
            return title;
        }
        public string RejectMarks(string text)
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
        public string DatetimeOnlyNumber(DateTime dateInput)
        {
            var date = RoundDown(dateInput.ToLocalTime(), TimeSpan.FromMinutes(10));
            string result = date.Year.ToString() + date.Month.ToString("D2") + date.Day.ToString("D2") + date.Hour.ToString("D2") + date.Minute.ToString("D2") + "00";
            return result;
        }
        public string GetDay(DateTime date)
        {
            string result = date.Year.ToString() + "/" + date.Month.ToString("D2") + "/" + date.Day.ToString("D2");
            return result;
        }
        public DateTime RoundDown(DateTime dt, TimeSpan d)
        {
            var delta = dt.Ticks % d.Ticks;

            return new DateTime(dt.Ticks - delta, dt.Kind);
        }
        public DateTime RoundUp(DateTime dt, TimeSpan d)
        {
            return new DateTime((dt.Ticks + d.Ticks - 1) / d.Ticks * d.Ticks, dt.Kind);
        }
        public int ThoiGianDelayDeBatDauChayService(DateTime input, double minute = 2)
        {
            var timeStart = RoundUp(input, TimeSpan.FromMinutes(10));
            TimeSpan span;
            if (minute == 0)
            {
                span = timeStart - input;
            }
            else
            {
                span = timeStart.AddMinutes(minute) - input;
            }
            int ms = (int)span.TotalMilliseconds;
            return ms;
        }
    }
    public static class Constant
    {
        public static int KhiTuong = 1;
        public static int ThuyVan = 2;
        public static int DoMua = 3;
        public static int DoGio = 4;
        public static int MuaNhiet = 5;
        public static string MaKhiTuong = "AWS";
        public static string MaThuyVan = "WL";
        public static string MaDoMua = "RAIN";
        public static string MaDoGio = "WIND";
        public static string MaMuaNhiet = "RAINTEMP";
        public static string MuaHoaBinh = "MUA_HOA_BINH";
        public static string Prefix_Device_HoaBinh = "AQRLG";
    }
    public static class Utility
    {
        public static double CheckNull(double? input)
        {
            if (input.HasValue)
            {
                return input.Value;
            }
            else
            {
                return -9999;
            }
        }
        public static string BuildCachedKey(params object[] objects)
        {
            StringBuilder stringBuilder = new StringBuilder();
            if (objects != null)
            {
                foreach (var item in objects)
                {
                    stringBuilder.AppendFormat("{0}_", item);
                }
            }
            return stringBuilder.ToString().TrimEnd('_');
        }
        public static DataReportDayValue CreateDataValue(
        string defaultValue = "0.0",
        string specialValue = null)
        {
            return new DataReportDayValue
            {
                Average = defaultValue,
                Min = defaultValue,
                Last = defaultValue,
                Max = specialValue ?? defaultValue,
                Sum = specialValue ?? defaultValue
            };
        }
        public static string GetLocalIpAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = host.AddressList.Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(x => x.ToString());
            return System.Text.Json.JsonSerializer.Serialize(ipAddress) ?? "Unknown IP";
        }
    }
}
