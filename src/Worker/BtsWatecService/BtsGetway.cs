using bts.udpgateway;
using BtsGetwayService.MSSQL.Entity;
using Core.Helper;
using Core.Logging;
using Core.Model;
using Core.MSSQL.Responsitory.Interface;
using Core.Setting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BtsGetwayService
{
    public class BtsGetway
    {
        private readonly ILogger<BtsGetway> _logger;
        public readonly IGroupData _groupData;
        private readonly ILoggingService _loggingService;
        public readonly ISiteData _siteData;
        public readonly IReportS10Data _reportS10Data;
        public readonly AppApiWatecSetting _appSetting;
        Helper helperUlti = new Helper();
        public BtsGetway(ILogger<BtsGetway> logger,
            IOptions<AppApiWatecSetting> option,
            IGroupData groupData,
            ISiteData siteData,
            ILoggingService loggingService,
            IReportS10Data reportS10Data)
        {
            _appSetting = option.Value;
            _groupData = groupData;
            _siteData = siteData;
            _loggingService = loggingService;
            _reportS10Data = reportS10Data;
            _logger = logger;
        }
        public async void SendFile(DateTime to, DateTime from, int groupId)
        {
            _logger.LogInformation("Start: {0}", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            List<RegionalGroup> lstGroup = new List<RegionalGroup>();
            List<RegionalGroup> lstGroupAll = _groupData.GetAll().ToList();
            if (_appSetting.IsChooseGroup == 1)
                lstGroup = lstGroupAll.Where(i => i.Id == groupId).ToList();
            else
                lstGroup = lstGroupAll;
            if (lstGroup != null && lstGroup.Count() > 0)
            {
                foreach (var grp in lstGroup)
                {
                    DateTime dateTime = from;
                    #region Lấy dữ liệu
                    List<WatecS10Model> listData = new List<WatecS10Model>();
                    List<string> lstTramKhongCoDuLieu = new List<string>();
                    try
                    {
                        List<Site> lstSite = _siteData.GetListSite(grp.Id).ToList();
                        foreach (var site in lstSite)
                        {
                            _logger.LogInformation("Get " + site.Name);
                            if (site.TypeSiteId == Constant.DoMua)
                            {
                                if (site.DeviceId.HasValue)
                                {
                                    var item = _reportS10Data.GetByTime(from, to, site.DeviceId.Value).LastOrDefault();
                                    if (item != null)
                                    {
                                        _logger.LogInformation("Push Data " + item);
                                        WatecS10Model modelFileS10Json = new WatecS10Model();
                                        modelFileS10Json.sid = item.DeviceId.Value.ToString("D10");
                                        modelFileS10Json.from = item.DateCreate.Value.ToString("yyyy-MM-dd HH:mm");
                                        modelFileS10Json.to = item.DateCreate.Value.ToString("yyyy-MM-dd HH:mm");
                                        modelFileS10Json.val = Utility.CheckNull(item.MRT);
                                        listData.Add(modelFileS10Json);
                                    }
                                    else
                                    {
                                        lstTramKhongCoDuLieu.Add(site.Name);
                                    }
                                }
                                else
                                {
                                    lstTramKhongCoDuLieu.Add(site.Name);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _loggingService.Error(ex);
                        _logger.LogError(null, ex);
                    }

                    if (lstTramKhongCoDuLieu.Count > 0)
                    {
                        var danhSachTramKhongCoDuLieu = string.Join(", ", lstTramKhongCoDuLieu);
                        _logger.LogWarning("Group {0}: {1} tram khong co du lieu trong khoang {2} - {3}: {4}",
                            grp.Name, lstTramKhongCoDuLieu.Count,
                            from.ToString("dd/MM/yyyy HH:mm:ss"), to.ToString("dd/MM/yyyy HH:mm:ss"),
                            danhSachTramKhongCoDuLieu);
                        _loggingService.Warn($"Group {grp.Name}: {lstTramKhongCoDuLieu.Count} tram khong co du lieu: {danhSachTramKhongCoDuLieu}");
                    }
                    #endregion

                    #region Send data api
                    if(listData.Count == 0)
                    {
                        _logger.LogInformation("Khong co du lieu de gui cho group " + grp.Name);
                        continue;
                    }
                    var result = await ApiSend.PostDataObject(_appSetting.ApiKey, _appSetting.UrlPost, listData);
                    _logger.LogInformation("Da gui {0} ban ghi cho group {1}. Ket qua: {2}/{3}", listData.Count, grp.Name, result.Code, result.Message);
                    _loggingService.Info($"Da gui {listData.Count} ban ghi cho group {grp.Name}. Ket qua: {result.Code}/{result.Message}");
                    #endregion
                }
            }
            _logger.LogInformation("End: {0}", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
        }
    }
}
