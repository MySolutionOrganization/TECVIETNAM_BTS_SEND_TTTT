# Primary Workflow

1. Xác định yêu cầu chính.
2. Xác định phạm vi ảnh hưởng.
3. Đọc rule liên quan.
4. Kiểm tra kiến trúc hiện tại của repo.
5. Đề xuất phương án ít rủi ro nhất.
6. Tạo nhánh mới trước khi thực hiện code.
7. Viết code theo convention hiện có.
8. Kiểm tra lỗi build hoặc lỗi logic có thể phát sinh.
9. Viết unitest kiểm tra kết quả.
10. Tóm tắt thay đổi.

## Khi tạo API mới

Dùng lệnh `/create-api` — xem `.claude/commands/create-api.md` để biết quy trình chi tiết và code template theo pattern của project.

**Thứ tự tạo file:**
1. Request class (kế thừa `PagingQuery`)
2. Handler class (cùng file với Request hoặc file riêng)
3. Đăng ký route trong Carter `ICarterModule` (`Endpoints/HoSoDienTu/`)

**Lưu ý kiến trúc:**
- Project dùng **Carter** (`ICarterModule`) thay vì MVC Controller.
- Response wrapper là `ResponseDetails<List<T>>`, không phải `Response<IEnumerable<T>>`.
- User context lấy qua `IHoSoDienTuProvider.UserProfile`, không phải `IAuthenticationProvider<Identity>`.
- Connection string appsetting: `HoSoDienTuSqlConnectionAppsetting`.
