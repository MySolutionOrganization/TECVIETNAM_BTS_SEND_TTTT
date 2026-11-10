using bts.udpgateway;
using BtsGetwayService.Core;
using BtsGetwayService.MSSQL.Entity;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Core.MSSQL.Responsitory.Interface
{
    public interface ISiteData : IDapperBaseData<Site>
    {
        IEnumerable<Site> GetListSite(int groupId);
        /// <summary>
        /// Lấy các trạm đang hoạt động của một group theo đúng loại trạm.
        /// Lọc ngay ở SQL thay vì lấy hết rồi lọc trong C#.
        /// </summary>
        IEnumerable<Site> GetListSiteByType(int groupId, int typeSiteId);
        Task<IEnumerable<Site>> GetDsSite(List<int> dsDeviceId);
        Task<Site> GetSiteByDeviceId(int deviceId);
        IEnumerable<Site> GetSite(int deviceid);
        void UpdateStatusActive(int deviceid);
        void UpdateStatusActive(List<int> lstDeviceid);
        void UpdateStatusDisable(List<int> lstDeviceid);
        Task<Site> GetByDeviceId(int deviceId);
    }
}
