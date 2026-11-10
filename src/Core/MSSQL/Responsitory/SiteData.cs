using BtsGetwayService.Core;
using BtsGetwayService.MSSQL.Entity;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Dapper;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace bts.udpgateway
{
    public class SiteData : DapperBaseData<Site>, ISiteData
    {

        public SiteData(IOptions<Connections> option) : base(option.Value.ConnectSqlString)
        {

        }
        public IEnumerable<Site> GetSite(int deviceid)
        {
            string query = string.Format(@"select Name from Site where DeviceId={0}", deviceid);
            return Query<Site>(query, null);
        }
        public void UpdateStatusActive(List<int> lstDeviceid)
        {           
            if (lstDeviceid != null && lstDeviceid.Count() > 0)
            {
                var strDevice = string.Join(",", lstDeviceid);
                var strDeviceQuery = strDevice.Remove(strDevice.Length - 1, 1);
                string query = string.Format(@"update site set IsActive=1,IsAlarm = 0 where DeviceId in({0})", strDevice);              
                Execute(query, null);
            }

        }
        public void UpdateStatusActive(int deviceid)
        {
            string query = string.Format(@"update site set IsActive=1 where DeviceId ={0}", deviceid);
            Execute(query, null);
        }
        public void UpdateStatusDisable(List<int> lstDeviceid)
        {     
            if (lstDeviceid != null && lstDeviceid.Count() > 0)
            {
                var strDevice = string.Join(",", lstDeviceid);
                var strDeviceQuery = strDevice.Remove(strDevice.Length - 1, 1);
                string query = string.Format(@"update site set IsActive=0, IsAlarm = 1 where DeviceId in({0})", strDevice);               
                Execute(query, null);
            }

        }
        public IEnumerable<Site> GetListSite(int groupId)
        {
            DynamicParameters listParameter = new DynamicParameters();
            listParameter.Add("@Group_ID", groupId);
            string query = string.Format(@"select * from Site where Group_Id=@Group_ID and IsActive=1", groupId);
            return Query<Site>(query, listParameter);
        }
        /// <summary>
        /// Lấy các trạm đang hoạt động của một group theo đúng loại trạm.
        /// Lọc ngay ở SQL thay vì lấy hết rồi lọc trong C#.
        /// </summary>
        public IEnumerable<Site> GetListSiteByType(int groupId, int typeSiteId)
        {
            DynamicParameters listParameter = new DynamicParameters();
            listParameter.Add("@Group_Id", groupId);
            listParameter.Add("@TypeSiteId", typeSiteId);
            string query = @"select * from Site
                             where Group_Id = @Group_Id and TypeSiteId = @TypeSiteId and IsActive = 1";
            return Query<Site>(query, listParameter);
        }
        public async Task<IEnumerable<Site>> GetDsSite(List<int> dsDeviceId)
        {
            DynamicParameters listParameter = new DynamicParameters();
            string query = string.Format(@"select * from Site s left join RegionalGroup rg on s.Group_Id = rg.Id");
            if (dsDeviceId.Any())
            {
                query +=string.Format(@" where s.DeviceId IN ({0})",string.Join(',',dsDeviceId));                
            }
            
            return await QueryAsync<Site>(query, listParameter);
        }
        public async Task<Site> GetSiteByDeviceId(int deviceId)
        {
            DynamicParameters listParameter = new DynamicParameters();
            listParameter.Add("@DeviceID", deviceId);
            string query = string.Format(@"select * from Site where DeviceId=@DeviceID and IsActive=1");
            return await QueryFirstOrDefaultAsync<Site>(query, listParameter);
        }
        public async Task<Site> GetByDeviceId(int deviceId)
        {
            DynamicParameters listParameter = new DynamicParameters();
            listParameter.Add("@DeviceID", deviceId);
            string query = string.Format(@"select * from Site where DeviceId=@DeviceID");
            return await QueryFirstOrDefaultAsync<Site>(query, listParameter);
        }
    }
}
