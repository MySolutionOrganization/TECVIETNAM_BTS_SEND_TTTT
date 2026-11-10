using bts.udpgateway;
using BtsGetwayService.Core;
using BtsGetwayService.MSSQL.Entity;
using Core.Model.Report.ReportDay;
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.MSSQL.Responsitory.Interface
{
    public interface IReportS10Data : IDapperBaseData<ReportS10>
    {
        IEnumerable<ReportS10> GetByTime(DateTime from, DateTime to, int deviceId);
        /// <summary>
        /// Lấy bản tin MỚI NHẤT trong khoảng [from, to) của từng thiết bị, gộp tất cả thiết bị
        /// vào một câu query thay vì mỗi thiết bị một lần gọi DB.
        /// Mỗi thiết bị có dữ liệu trả về đúng 1 bản ghi; thiết bị không có dữ liệu thì không xuất hiện.
        /// </summary>
        IEnumerable<ReportS10> GetLatestInRange(DateTime from, DateTime to, IEnumerable<int> deviceIds);
        ReportS10 GetLatest(int deviceId);
        List<DataReportDayEntity> GetReportByDay(DateTime fromDate, DateTime toDate, string sensortarget, string dsIdThietBi, int type);
        List<DataReportDayEntity> GetReportSumByTime(DateTime fromDate, DateTime toDate, string sensortarget, string dsIdThietBi, int type);
    }
}
