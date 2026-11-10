# Tối ưu BtsWatecService — Thiết kế

Ngày: 2026-11-10

## Bối cảnh

`BtsWatecService` là Windows Service chạy 10 phút/lần, đọc bảng `ReportS10` (SQL Server)
và đẩy số liệu mưa của các trạm `TypeSiteId = 3 (DoMua)` lên API Watec
(`https://igw.vrain.vn/m`).

Vừa bổ sung logic: mất 1-2 bản tin thì vẫn phát với `val = 0`, mất bản tin thứ 3 liên tiếp
thì dừng phát cho trạm đó và cảnh báo Telegram một lần. Logic đó giữ nguyên, không nằm
trong phạm vi thay đổi của tài liệu này.

## Vấn đề đã xác định khi đọc code

### Hiệu năng
1. N+1 query: mỗi trạm 1 lần gọi `IReportS10Data.GetByTime`, mỗi lần mở một `SqlConnection`
   mới (`DapperBaseData.Query`). N trạm × 144 chu kỳ/ngày.
2. `ReportS10Data.GetByTime` nối chuỗi SQL bằng `string.Format` → không tham số hoá,
   SQL Server không cache được execution plan. Kèm `select *` và `join Site` hoàn toàn thừa
   (không dùng cột nào của `Site`, lại gây trùng tên cột khi Dapper map).
3. `SiteData.GetListSite` lấy toàn bộ site của group rồi mới lọc `TypeSiteId == DoMua` trong C#.
4. `Core.Helper.ApiSend.PostDataObject` tạo `new HttpClient()` mỗi lần gọi → cạn socket/TIME_WAIT.

### Đúng đắn / vận hành
5. `ApiSend` nuốt toàn bộ exception (`catch (Exception) { Code = "500"; }`), không log,
   không retry → API Watec lỗi tạm thời là mất nguyên lô 10 phút.
6. `ApiSend` dùng `.Result` (sync-over-async).
7. `Worker.StartAsync` `await Task.Delay(...)` **trước** `base.StartAsync` → chặn khởi động host
   tới ~11 phút. Windows SCM timeout mặc định 30s → service có thể bị báo "failed to start".
8. `Console.ReadLine()` trong `StartAsync` → treo vĩnh viễn nếu chạy dạng Windows Service
   với `IsChooseGroup = 1`.
9. Cửa sổ truy vấn thực tế chỉ ~1 phút: service khởi động ở mốc 10 phút **+1 phút** rồi query
   `[RoundDown(now,10m), +10m]`. Chạy lúc 10:11 → query `[10:10, 10:20]` nhưng mới là 10:11.
   Trạm gửi trễ quá 1 phút bị coi là mất bản tin, và bản tin thật đó vĩnh viễn không được phát.
   **Đây là nguyên nhân gốc của hiện tượng "thỉnh thoảng mất 1-2 bản tin".**
10. `order by r.id desc` rồi `.LastOrDefault()` → lấy bản tin **cũ nhất** trong cửa sổ.

### Dọn dẹp
11. `Helper helperUlti` và `DateTime dateTime` khai báo nhưng không dùng.
12. `LogInformation("Get " + site.Name)` chạy cho mọi site mọi chu kỳ → rác log Elasticsearch.
    `"Push Data " + item` chỉ in ra tên class.
13. `IZipHelper`, `using Core.MessageQueue`, `using Core.Interfaces` thừa.
14. `Microsoft.Extensions.Hosting` 6.0.0 trên `net8.0`.

## Quyết định đã chốt

| Quyết định | Chọn |
|---|---|
| Phạm vi đụng Core | Chỉ **thêm** method mới, giữ nguyên 100% method cũ |
| Cửa sổ truy vấn | Query chu kỳ **liền trước** (đã đóng) |
| Chọn bản tin trong cửa sổ | Bản **mới nhất** theo `DateCreate` |
| Tổ chức code | Tách `WatecApiClient` và `RainDataCollector` ra khỏi `BtsGetway` |
| Kênh cảnh báo | Telegram + log (đã làm ở thay đổi trước) |

Lý do giữ nguyên method Core cũ: `GetByTime` đang được dùng ở 25 chỗ
(`BtsS10SendTTTT`, `GetDataByMonth`, `Api_Watec`, `Infrastructure`), `GetListSite` 6 chỗ,
`ApiSend.PostDataObject` cả `Infrastructure`. Sửa tại chỗ buộc phải kiểm tra lại toàn bộ
các service đó, trong khi repo không có test gate.

## Kiến trúc

```
src/Worker/BtsWatecService/
  Program.cs              DI: bỏ đăng ký thừa, bật AddHttpClient
  Worker.cs               vòng lặp 10 phút, StartAsync không còn chặn
  WatecTimeWindow.cs      MỚI: tính cửa sổ thời gian (thuần, test được offline)
  RainDataCollector.cs    MỚI: lấy site đo mưa, 1 query gộp, dựng model, đếm mất bản tin
  WatecApiClient.cs       MỚI: POST + retry + log lỗi
  BtsGetway.cs            điều phối: lặp group → collector → api client → log tổng hợp
  MissingDataTracker.cs   giữ nguyên
```

Mỗi đơn vị có một trách nhiệm và một interface rõ ràng:

- `WatecTimeWindow` — hàm thuần, không phụ thuộc gì, trả về `(from, to)` của chu kỳ đã đóng.
- `IWatecApiClient` — biết gửi `List<WatecS10Model>` lên Watec và nói kết quả. Không biết gì
  về trạm, bộ đếm hay SQL.
- `RainDataCollector` — biết lấy số liệu một group và trả về `RainCollectResult`. Không biết
  gì về HTTP.
- `BtsGetway` — điều phối hai cái trên và log tổng hợp. Không chứa SQL, không chứa HTTP.

## Thay đổi chi tiết

### 1. Core — thêm method (không sửa method cũ)

```csharp
// ISiteData
IEnumerable<Site> GetListSiteByType(int groupId, int typeSiteId);

// IReportS10Data
IEnumerable<ReportS10> GetLatestInRange(DateTime from, DateTime to, IEnumerable<int> deviceIds);
```

`GetLatestInRange` — một query cho cả group, tham số hoá hoàn toàn:

```sql
SELECT t.* FROM (
    SELECT r.*, ROW_NUMBER() OVER (
        PARTITION BY r.DeviceId ORDER BY r.DateCreate DESC, r.ID DESC) AS rn
    FROM ReportS10 r
    WHERE r.DateCreate >= @From AND r.DateCreate < @To AND r.DeviceId IN @DeviceIds
) t WHERE t.rn = 1
```

Chia lô 1000 `deviceId`/lần để không chạm giới hạn 2100 tham số của SQL Server.
Danh sách rỗng trả về rỗng ngay, không gọi DB.

### 2. Cửa sổ thời gian

`to = RoundDown(now, 10m)`, `from = to - 10m`.
Chạy lúc 10:11 → `[10:00, 10:10)`. Cửa sổ đã đóng hoàn toàn, trạm có đủ 10 phút
cộng 1 phút ân hạn.

Điều kiện SQL đổi từ `between from and to` (đóng hai đầu → bản ghi đúng mốc `to` bị đếm
ở cả hai chu kỳ) sang `>= from AND < to`.

Hệ quả: số liệu lên Watec trễ thêm ~10 phút. Timestamp gửi đi vẫn là `DateCreate` thật của
bản tin nên Watec nhận đúng mốc thời gian.

### 3. WatecApiClient

- `IHttpClientFactory`, named client `"watec"`, header `x-api-key` và timeout 30s gắn sẵn
  lúc đăng ký DI.
- Retry tối đa 3 lần, backoff 2s rồi 4s.
- **Chỉ** retry lỗi mạng (`HttpRequestException`), timeout (`TaskCanceledException`) và 5xx.
  4xx không retry: sai key hoặc sai payload thì thử lại vô ích.
- Trả về `WatecSendResult { Success, StatusCode, Message, AttemptCount }`.
- Log đầy đủ nội dung lỗi thay vì nuốt im.
- Không đụng `Core.Helper.ApiSend`.

### 4. Lifecycle

- `StartAsync` trả về ngay, không `await Task.Delay`. Host khởi động dưới 1s.
- Canh mốc 10 phút chuyển vào đầu `ExecuteAsync`.
- `Console.ReadLine()` chỉ chạy khi `Environment.UserInteractive`; chạy dạng service thì
  log cảnh báo và gửi toàn bộ group.
- Vòng lặp dùng `PeriodicTimer(10 phút)` thay `Task.WhenAll(work, Task.Delay)`.
- `OperationCanceledException` được bắt để stop sạch.

### 5. Logging

- Bỏ `LogInformation("Get " + site.Name)` và `"Push Data " + item`.
- Structured logging (`{DeviceId}`, `{SiteName}`) thay `{0}` để Elasticsearch lọc được.
- Giữ nguyên các dòng tổng hợp cuối mỗi group: không có dữ liệu / bù 0 / dừng phát / kết quả gửi.

### 6. Dọn dẹp

Bỏ `Helper helperUlti`, `DateTime dateTime`, `IZipHelper`, using thừa.
`public readonly` → `private readonly`. Nâng `Microsoft.Extensions.Hosting` lên 8.0.x.

## Không thay đổi

- Logic bù `val = 0` khi mất 1-2 bản tin và dừng phát + cảnh báo khi mất bản tin thứ 3.
- `MissingDataTracker`.
- Format payload `WatecS10Model` (`sid` / `from` / `to` / `val`).
- Tên class `BtsGetway`, tên service, các key trong `appsettings.json`.
- Toàn bộ method Core cũ.

## Xử lý lỗi

| Tình huống | Hành vi |
|---|---|
| Lỗi SQL khi lấy site/số liệu một group | Log, bỏ qua group đó, các group khác vẫn chạy |
| API Watec lỗi mạng/5xx | Retry 3 lần; hết retry thì log Fatal, lô 10 phút đó mất |
| API Watec trả 4xx | Không retry, log Fatal kèm status + body |
| Telegram lỗi | Nuốt và log, không ảnh hưởng luồng phát số liệu |
| Service stop giữa chừng | `OperationCanceledException` → thoát vòng lặp, không log như lỗi |

## Kiểm chứng

- `dotnet build` cho `BtsWatecService` và mọi project `net8.0` dùng `Core`
  (`BtsS10SendTTTT`, `CheckActiveDevice`, `Infrastructure`).
- Unit test thuần (không cần DB) cho `MissingDataTracker` và `WatecTimeWindow`
  trong project mới `src/Test/BtsWatecService.Tests` (`net8.0`, xUnit).
- Các lỗi `NU1201` sẵn có của `Api_Watec` / `BtsSendTTTT` / `GatewayApi` / `GetDataByMonth` /
  `UnitTest` là do lệch TargetFramework có từ trước, không nằm trong phạm vi này.

## Rủi ro đã biết

- `MissingDataTracker` giữ trạng thái trong RAM: restart service thì bộ đếm về 0, một trạm
  đang bị dừng phát sẽ phát lại 2 bản tin `0` rồi mới dừng và cảnh báo lại.
- Đổi từ bản tin cũ nhất sang mới nhất là thay đổi hành vi với số liệu Watec nhận được;
  đã được xác nhận là đúng mong muốn.
- Số liệu lên Watec trễ thêm ~10 phút so với hiện tại.
