# Tối ưu BtsS10SendTTTT — Thiết kế

Ngày: 2026-11-10

Tài liệu cùng bộ với [2026-11-10-btswatecservice-toi-uu-design.md](2026-11-10-btswatecservice-toi-uu-design.md).
Nhiều quyết định ở đây kế thừa từ tài liệu đó.

## Bối cảnh

`BtsS10SendTTTT` là Windows Service chạy 10 phút/lần, đọc bảng `ReportS10` (SQL Server),
xuất số liệu của 5 loại trạm thành 5 file JSON rồi tải lên FTP của TTTT.

Mỗi group (`RegionalGroup` có `IsSendTTTT = 1`) có thông tin FTP riêng
(`FtpIp` / `FtpAccount` / `FtpPassword` / `FtpDirectory`) lấy từ DB.

Năm loại trạm và mã dùng trong tên file:

| `TypeSiteId` | Loại | Mã file | `StationNo` |
|---|---|---|---|
| 1 | Khí tượng | `AWS` | `D5` |
| 2 | Thuỷ văn | `WL` | `D5` |
| 3 | Đo mưa | `RAIN` | `D6` |
| 4 | Đo gió | `WIND` | `D6` |
| 5 | Mưa nhiệt | `RAINTEMP` | `D5` |

Tên file: `{grp.Code}_{mã loại}_{timestamp}.json`.

## Vấn đề đã xác định khi đọc code

### Bug logic

1. **`dateTimeStr` và `dateTime` dùng chung cho cả 5 loại trạm.**
   Khai báo ngoài vòng lặp trạm rồi bị ghi đè trong **mọi** nhánh loại trạm. Hết vòng lặp
   chúng mang giá trị của **trạm cuối cùng có dữ liệu, bất kể loại nào**, rồi được dùng đặt tên
   cho cả 5 file và chọn thư mục ngày. File `AWS` có thể mang timestamp của một trạm thuỷ văn;
   qua ranh giới ngày (23:5x ↔ 00:0x) file rơi vào thư mục sai ngày.

2. **Một exception là chết cả service.**
   `grp.FtpAccount.Trim()` gọi `.Trim()` **trước** khi kiểm tra `IsNullOrEmpty`
   → `NullReferenceException` nếu DB để NULL. `Directory.CreateDirectory` cũng nằm ngoài mọi
   try/catch. Từ .NET 6, exception thoát khỏi `ExecuteAsync` sẽ dừng toàn bộ host
   (`BackgroundServiceExceptionBehavior.StopHost` là mặc định).

3. **Cửa sổ truy vấn chỉ ~18 giây.**
   `ThoiGianDelayDeBatDauChayService(now, 0.3)` = mốc 10 phút + 0.3 phút.

4. **Cửa sổ lệch pha.** `from = RoundDown(now, 5 phút)` nhưng `to = from + 10 phút` — cửa sổ
   10 phút neo vào mốc 5 phút. Cộng `between` đóng hai đầu → bản ghi đúng mốc bị đếm ở hai cửa sổ.

5. **`if (!File.Exists(path)) File.WriteAllText(...)`** — file đã tồn tại thì không ghi gì,
   nhưng vẫn FTP upload file cũ. Trùng tên là âm thầm gửi lại số liệu cũ.

6. **FTP không chịu được lỗi mạng.** `CreateFTPDirectory` ép kiểu `(FtpWebResponse)ex.Response`
   mà không kiểm tra null → `NullReferenceException` khi không có response.
   `DoesFtpDirectoryExist` nuốt mọi `WebException` khác → lỗi mạng bị hiểu nhầm thành
   "thư mục chưa tồn tại".

7. **`float.Parse(Math.Round(x,2).ToString())`** — round-trip qua string phụ thuộc culture
   của máy chạy, trong khi chỉ cần ép kiểu.

### Hiệu năng và cấu trúc

8. N+1 query: mỗi trạm một lần gọi `GetByTime`, mỗi lần mở một `SqlConnection` mới.
   Nặng hơn `BtsWatecService` vì có 5 loại trạm.
9. `SendFile` dài 315 dòng; 5 khối serialize + ghi file + FTP **copy-paste gần như giống hệt nhau**.
10. **`BtsGetway` không inject `ILogger`** — chỉ dùng `_loggingService` (log4net). Nghĩa là
    `AddSerilogElasticsearch` trong `Program.cs` không nhận được log nào từ `BtsGetway`;
    Elasticsearch đang mù với service này.
11. `GetAllFtpFiles`, `CreateFile`, `DeleteFile` là dead code (`DeleteFile` còn xoá thư mục FTP).
    `throw ex;` làm mất stack trace gốc.
12. `WebClient` và `FtpWebRequest` đều obsolete từ .NET 6 (SYSLIB0014).
13. `StartAsync` chặn; `Microsoft.Extensions.Hosting` 6.0.0 trên `net8.0`; using thừa
    (`BtsGetwayService.Interface`, `BtsGetwayService.Service`, `Core.MongoDb.Data.Interface`,
    `ES_CapDien.AppCode`).

## Quyết định đã chốt

| Quyết định | Chọn | Ghi chú |
|---|---|---|
| Bug 1 — timestamp dùng chung | **Giữ nguyên hành vi** | Sửa sẽ đổi tên file bên TTTT nhận; cần thống nhất với TTTT trước |
| Cửa sổ truy vấn | Chu kỳ **liền trước** (đã đóng) | Giống `BtsWatecService` |
| Thư viện FTP | **Chuyển sang FluentFTP** 55.1.0 | `WebClient`/`FtpWebRequest` đã obsolete |
| Phạm vi đụng Core | Chỉ **thêm**, không sửa method cũ | Kế thừa từ tài liệu `BtsWatecService` |

Bug 1 được giữ nguyên **có chủ đích**. Để nó không bị "sửa nhầm" về sau:
- Có chú thích XML trong `S10CollectResult.DateTimeStr` giải thích đây là cố ý.
- Có test `DateTimeStr_lay_tu_tram_cuoi_cung_co_du_lieu_bat_ke_loai` khoá hành vi lại.

Khi nào thống nhất được với TTTT thì xoá test đó là đổi được.

## Kiến trúc

```
src/Worker/BtsS10SendTTTT/
  Program.cs            DI, dọn using thừa
  Worker.cs             vòng lặp 10 phút, StartAsync không chặn
  BtsGetway.cs          điều phối: lặp group -> lấy số liệu -> ghi file -> FTP
  S10DataCollector.cs   MỚI: hàm THUẦN, map ReportS10 -> 5 loại model JSON
  FtpUploader.cs        MỚI: IFtpUploader, FluentFTP + retry + log

src/Test/BtsS10SendTTTT.Tests/   MỚI (net8.0, xUnit)
```

Core — dùng lại, không thêm gì mới cho service này:
- `IReportS10Data.GetLatestInRange` (đã thêm khi làm `BtsWatecService`)
- `ReportTimeWindow` (xem mục "Dùng chung với BtsWatecService")

Ranh giới trách nhiệm:

- `S10DataCollector` — nhận sẵn danh sách trạm và dictionary bản tin, trả về các nhóm bản ghi.
  Không đụng DB, file hay FTP, nên test được offline. Đây là lý do bộ test có 21 ca cho riêng nó
  và phần ghép đường dẫn.
- `IFtpUploader` — biết tải một file lên một `FtpTarget`. Không biết gì về trạm hay số liệu.
- `BtsGetway` — điều phối, ghi file, log tổng hợp.

## Thay đổi chi tiết

### 1. Cửa sổ thời gian

`to = RoundDown(now, 10m)`, `from = to - 10m`, ân hạn 1 phút.
Chạy lúc 10:11 → `[10:00, 10:10)`. Thay cho cửa sổ ~18 giây neo lệch pha của bản cũ.

Điều kiện SQL là `>= From AND < To` (do `GetLatestInRange` cung cấp), thay cho `between`
đóng hai đầu.

### 2. Truy vấn gộp

Một query cho cả group thay vì mỗi trạm một lần gọi DB, dùng lại `GetLatestInRange`:

```sql
SELECT t.* FROM (
    SELECT r.*, ROW_NUMBER() OVER (
        PARTITION BY r.DeviceId ORDER BY r.DateCreate DESC, r.ID DESC) AS rn
    FROM ReportS10 r
    WHERE r.DateCreate >= @From AND r.DateCreate < @To AND r.DeviceId IN @DeviceIds
) t WHERE t.rn = 1
```

**Vẫn dùng `GetListSite` cũ** (lấy mọi loại trạm) chứ không dùng `GetListSiteByType`:
thứ tự duyệt trạm quyết định timestamp đặt tên file, nên phải giữ nguyên thứ tự truy vấn cũ.

### 3. S10DataCollector

```csharp
S10CollectResult Build(
    IEnumerable<Site> sites,
    IReadOnlyDictionary<int, ReportS10> banTinTheoDevice,
    ReportTimeWindow window)
```

Hàm thuần. Duyệt trạm theo đúng thứ tự `GetListSite` trả về, map sang model JSON theo
`TypeSiteId`, và cập nhật `DateTimeStr` / `MocThoiGian` sau mỗi trạm có dữ liệu — tái hiện
chính xác hành vi "trạm cuối cùng thắng" của bản cũ.

Trạm chưa gán `DeviceId`, trạm không có bản tin, bản tin thiếu `DateCreate`, và loại trạm
ngoài 5 loại đều được bỏ qua một cách tường minh.

`CacNhomCoDuLieu()` trả về các nhóm có bản ghi theo đúng thứ tự xuất file của bản cũ:
AWS → WIND → RAIN → RAINTEMP → WL.

Mực nước: `(float)Math.Round(item.MFL.Value, 2)` thay cho round-trip qua string.

### 4. FtpUploader

- FluentFTP 55.1.0, `AsyncFtpClient`, `AutoConnect`.
- `UploadFile(..., FtpRemoteExists.Overwrite, createRemoteDir: true, ...)` — thư viện tự tạo
  thư mục remote, bỏ hẳn cặp `DoesFtpDirectoryExist` / `CreateFTPDirectory` đầy lỗi của bản cũ.
- Retry 3 lần, backoff 2s rồi 4s. Hết retry thì log `Fatal` và trả `false`.
- `TachHostVaPort(string ftpIp)` — `FtpIp` trong DB không có định dạng thống nhất
  (`1.2.3.4`, `ftp://1.2.3.4`, `ftp://1.2.3.4:2121/`...), nên tách host và port tường minh.
- `FtpTarget.HopLe` — thiếu host / user / password thì bỏ qua group, log cảnh báo,
  không ném exception.

### 5. Ghép đường dẫn cục bộ

`FtpDirectory` trong DB thường bắt đầu bằng `/` (bản cũ ghép thẳng vào URL `ftp://host` + thư mục).
`Path.Combine` **vứt bỏ các đoạn đứng trước** khi gặp đoạn bắt đầu bằng dấu phân cách, nên
file sẽ bị ghi ra ngoài `FolderLuuTruFile`.

`BtsGetway.GhepThuMucLocal` cắt dấu phân cách đầu của từng đoạn trước khi ghép. Có 6 test khoá lại.

> Đây là lỗi phát sinh trong quá trình refactor (bản cũ dùng nối chuỗi nên không dính),
> được phát hiện khi tự review và đã sửa trước khi bàn giao.

### 6. Ghi file

Luôn ghi đè thay vì `if (!File.Exists)`. Tên file **không đổi**, chỉ đảm bảo nội dung gửi đi
khớp số liệu vừa lấy.

Thư mục chỉ được tạo khi có ít nhất một trạm có dữ liệu — bản cũ tạo thư mục rỗng mỗi chu kỳ.

`IsSendTTTT = 0` thì vẫn ghi file cục bộ nhưng không gọi FTP (giữ nguyên hành vi cũ).

### 7. Xử lý lỗi và lifecycle

- `StartAsync` trả về ngay; canh mốc chu kỳ nằm trong `ExecuteAsync`.
- Vòng lặp dùng `PeriodicTimer(10 phút)` thay `Task.WhenAll(work, Task.Delay)`.
- Mỗi group bọc try/catch riêng, cả chu kỳ bọc thêm một lớp — không có đường nào để exception
  thoát ra `ExecuteAsync` và dừng host.
- `OperationCanceledException` được phân biệt với lỗi thật để stop sạch.

### 8. Logging

- `BtsGetway` và `FtpUploader` inject `ILogger<T>` → log lên Elasticsearch qua Serilog.
- Structured logging (`{GroupName}`, `{FileName}`, `{Window}`).
- Giữ `_loggingService` (log4net) cho các mức Warn/Error/Fatal như các service khác trong repo.

### 9. Dọn dẹp

Xoá `GetAllFtpFiles`, `CreateFile`, `DeleteFile` (dead code). Bỏ `throw ex`.
Dọn using thừa. `Microsoft.Extensions.Hosting` 6.0.0 → 8.0.0.

## Dùng chung với BtsWatecService

`WatecTimeWindow` được chuyển từ `src/Worker/BtsWatecService/` lên
`src/Core/Helper/ReportTimeWindow.cs` (namespace `BtsGetwayService`) để hai service dùng chung
một logic canh mốc chu kỳ.

Lý do: nhân bản chính là cách repo này có 5 bản `BtsGetway` gần giống nhau. Logic cửa sổ thời gian
có một lỗi tinh vi (xem mục Rủi ro) nên càng không nên tồn tại hai bản.

`BtsWatecService` chỉ phải đổi tên kiểu, 18 test của nó vẫn xanh sau khi chuyển.

## Không thay đổi

- Hành vi đặt tên file và chọn thư mục ngày (bug 1) — **có chủ đích**, xem mục Quyết định.
- Định dạng 5 model JSON (`ModelFile*S10Json`) và các giá trị sentinel (`-9999` cho null).
- Thứ tự xuất file.
- Hành vi `IsSendTTTT = 0`.
- Tên class `BtsGetway`, tên service, các key trong `appsettings.json`.
- Toàn bộ method Core cũ.

## Xử lý lỗi

| Tình huống | Hành vi |
|---|---|
| Group thiếu thông tin FTP | Log cảnh báo, bỏ qua group, không ném exception |
| Lỗi SQL khi lấy số liệu một group | Log, bỏ qua group đó, các group khác vẫn chạy |
| Lỗi ghi file JSON | Log, bỏ qua file đó, các file khác vẫn được xuất |
| FTP lỗi mạng / timeout | Retry 3 lần; hết retry thì log Fatal, file vẫn còn ở thư mục cục bộ |
| Không trạm nào có dữ liệu | Log thông tin, không tạo thư mục, không tạo file |
| Service stop giữa chừng | `OperationCanceledException` → thoát vòng lặp, không log như lỗi |

## Kiểm chứng

- `dotnet build` 0 lỗi: `Core`, `Infrastructure`, `BtsWatecService`, `BtsS10SendTTTT`,
  `CheckActiveDevice`.
- `src/Test/BtsS10SendTTTT.Tests` — 33 test, không cần DB/FTP/file:
  - `S10DataCollectorTests` (15): phân loại 5 loại trạm, trạm thiếu dữ liệu, định dạng
    `StationNo`, làm tròn mực nước, sentinel `-9999`, `Datadate` 14 chữ số không bị ký hiệu
    khoa học, thứ tự nhóm file, và **khoá hành vi "trạm cuối cùng quyết định timestamp"**.
  - `FtpUploaderTests` (3 nhóm, 12 ca): tách host/port cho mọi dạng `FtpIp` đang có trong DB,
    và kiểm tra `FtpTarget.HopLe` khi thiếu thông tin đăng nhập.
  - `DuongDanLocalTests` (6): ghép đường dẫn cục bộ khi `FtpDirectory` bắt đầu bằng `/` hoặc `\`.
- `src/Test/BtsWatecService.Tests` — 18 test, vẫn xanh sau khi chuyển `ReportTimeWindow` lên Core.

Các lỗi `NU1201` sẵn có của `Api_Watec` / `BtsSendTTTT` / `GatewayApi` / `GetDataByMonth` /
`UnitTest` là do lệch TargetFramework có từ trước, không nằm trong phạm vi này.

## Rủi ro đã biết

- **Chưa chạy với DB và FTP thật.** Test chỉ phủ phần thuần. Câu `GetLatestInRange` trên
  SQL Server và toàn bộ luồng FluentFTP với server TTTT **chưa được chạy lần nào**.
  FluentFTP đổi cả thư viện lẫn cách tạo thư mục remote (`createRemoteDir` thay cho
  `MakeDirectory` thủ công), nên **bắt buộc chạy thử một chu kỳ với
  `appsettings.Development.json`** và kiểm tra file lên đúng thư mục trước khi đẩy production.
- **File sang TTTT trễ thêm ~10 phút** do chuyển sang chu kỳ liền trước.
- **Bug 1 vẫn còn** theo quyết định: tên file và thư mục ngày của cả 5 loại vẫn lấy theo trạm
  cuối cùng có dữ liệu. Qua ranh giới ngày vẫn có thể ghi vào thư mục sai ngày.
- `ReportTimeWindow.DelayToiMocTiepTheo` từng có lỗi bỏ lỡ một cửa sổ khi khởi động
  (tính ra mốc chạy của chu kỳ sau thay vì mốc vừa đóng). Đã sửa và có test; đây là lý do
  logic này chỉ nên tồn tại ở một nơi trong Core.
- `FtpUploader` mở kết nối FTP mới cho mỗi file. Với số file hiện tại (tối đa 5 file/group/chu kỳ)
  thì không đáng kể, nhưng nếu số group tăng nhiều thì nên gom theo group và dùng lại kết nối.
