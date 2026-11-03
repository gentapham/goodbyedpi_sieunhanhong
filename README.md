# Siêu Nhân Hồng — Mở khoá Steam

![Giao diện](docs/screenshot.png)

App Windows giúp vào Steam, đăng nhập và cập nhật game khi nhà mạng chặn Steam bằng DPI (lọc theo tên miền SNI).
Bên dưới dùng [GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI): **không VPN, không lag, tốc độ tải giữ nguyên**.

## Tải về
Vào mục [**Releases**](../../releases/latest) và tải `SieuNhanHong.exe`. Chỉ cần đúng một file này.

## Tính năng
- **Bật/tắt bằng một nút** — tách gói bắt tay TLS để thiết bị lọc của nhà mạng không đọc được tên miền.
- **Chọn app được mở khoá**: Steam (mặc định), Epic Games, Discord, Battle.net, EA app, Ubisoft Connect, GOG Galaxy, Riot — hoặc mọi trang web. Có thể thêm trang web riêng.
- **Thu nhỏ xuống khay** (cạnh đồng hồ): biểu tượng mũ hồng khi đang bật, xám khi tắt; nhấn chuột phải để bật/tắt nhanh.
- **Tự bật khi mở máy** — chạy ngầm dưới dạng dịch vụ và hiện biểu tượng ở khay.
- **Chế độ mạnh** (`-5`) dự phòng khi chế độ thường không đủ.
- **Tự cập nhật**: cả app Siêu Nhân Hồng (từ Releases của repo này) lẫn GoodbyeDPI (từ ValdikSS/GoodbyeDPI).
- **Kiểm tra kết nối** tới Cửa hàng, Cộng đồng, Đăng nhập và máy chủ cập nhật game của Steam.

## Yêu cầu máy
- Windows 10 hoặc 11 bản 64-bit.
- .NET Framework 4.5 trở lên (Windows 10/11 có sẵn 4.8). Nếu máy thiếu, app sẽ báo và mở trang tải chính thức của Microsoft.
- Nếu máy chưa cài Steam, app hiện nút **Tải Steam** dẫn tới trang tải chính thức.

## Cài đặt & sử dụng
1. Tải `SieuNhanHong.exe`, chạy (app tự xin quyền Administrator).
2. Bấm nút **BIẾN HÌNH**. Lần đầu app tự giải nén GoodbyeDPI vào `C:\GoodbyeDPI`.
3. Mở Steam.

Gỡ bỏ: tắt nút trong app (tự gỡ chế độ tự bật), rồi xoá thư mục `C:\GoodbyeDPI`.

## Build từ mã nguồn
Không cần IDE — chỉ cần .NET Framework 4.x có sẵn trong Windows:

```bat
build.bat
```

## Phát hành bản mới
1. Tăng `AssemblyVersion` trong `SieuNhanHong.cs` (ví dụ `4.2.0.0`).
2. Commit và push lên `main`.

GitHub Actions tự build `SieuNhanHong.exe`; nếu phiên bản đó chưa có bản phát hành, nó tự tạo tag `v4.2.0` và đăng lên Releases. App của người dùng sẽ thấy bản mới và cập nhật bằng một nút bấm.

## Lưu ý
- GoodbyeDPI dùng driver WinDivert nên một số phần mềm diệt virus có thể báo nhầm.
- Anti-cheat của Valorant (Vanguard) và FACEIT có thể không chấp nhận WinDivert — tắt app trước khi chơi các game đó.

## Giấy phép bên thứ ba
- GoodbyeDPI — Apache License 2.0
- WinDivert — LGPL v3

Toàn văn giấy phép nằm trong `goodbyedpi/licenses/`.
