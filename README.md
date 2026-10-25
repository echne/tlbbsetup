# TLBB Phong Vân Kiếm — Launcher

Ứng dụng WPF dành cho Windows 10/11 x64, dùng để tải và cài đặt client TLBB Phong Vân Kiếm từ manifest HTTPS. Giao diện theo mô hình MVVM; phần tải xuống, xác minh, giải nén, cài đặt, shortcut, Defender và chạy game được tách thành các service riêng.

## Tính năng

- Mỗi lần mở launcher bắt đầu ở bước Thiết lập. Thư mục cài đặt và tùy chọn được lưu lại; dữ liệu tải dở có thể được tiếp tục khi bắt đầu lại nếu máy chủ hỗ trợ HTTP Range và ETag chưa thay đổi.
- Tải các volume song song, tối đa 3 luồng; hỗ trợ retry, tải tiếp file .part và xác minh kích thước cùng SHA-256 của từng volume.
- Giải nén archive split ZIP vào vùng staging, kiểm tra đường dẫn an toàn và hoàn tất cài đặt theo transaction có khả năng rollback.
- Tạo shortcut Desktop trỏ đến Launch.exe nếu người dùng chọn.
- Hiển thị tiến trình tổng theo các giai đoạn tải, xác minh, giải nén, hoàn thiện và dọn dẹp. Chỉ báo 100% sau khi cài đặt và dọn dữ liệu tạm thành công.
- Có thể thêm đúng thư mục game vào loại trừ Microsoft Defender sau khi người dùng chọn tùy chọn và xác nhận UAC. Tùy chọn bị vô hiệu hóa khi Defender không hoạt động, phát hiện antivirus khác hoặc không đọc được trạng thái bảo mật.
- Khi cài thành công, nút Bắt đầu chơi chạy Launch.exe và đóng launcher setup.

## Yêu cầu phát triển

- Windows 10/11 x64.
- .NET 10 SDK.
- Visual Studio có workload phát triển desktop .NET nếu muốn mở solution bằng Visual Studio.

Người chơi không cần cài .NET khi dùng bản publish tự chứa.

## Build và chạy bản phát triển

Mở PowerShell tại thư mục dự án:

    dotnet restore .\TLBB.PhongVanKiem.Launcher.sln
    dotnet build .\TLBB.PhongVanKiem.Launcher.sln -c Release
    dotnet run --project .\src\TLBB.Launcher\TLBB.Launcher.csproj

Solution có project kiểm thử tại tests\TLBB.Launcher.Tests. Có thể chạy bằng:

    dotnet test .\TLBB.PhongVanKiem.Launcher.sln -c Release

## Publish một file EXE

Chạy script:

    .\publish-win-x64.ps1

File đầu ra:

    .\artifacts\win-x64-onefile\TLBB.PhongVanKiem.Launcher.exe

Bản publish là self-contained, x64 và gom runtime cùng dependencies vào một EXE. Máy người chơi không cần cài .NET. EXE vẫn có thể giải nén một số thành phần native vào thư mục tạm khi chạy. Script kiểm tra thư mục đầu ra chỉ có EXE launcher; nếu còn file khác, script sẽ báo lỗi thay vì tự xóa chúng.

## Cấu hình manifest

Cấu hình endpoint manifest và host được phép nhận redirect nằm trong:

    src\TLBB.Launcher\Config\appsettings.json

Cấu hình này được nhúng vào EXE, vì vậy đổi URL manifest hoặc trusted host cần build lại launcher. Cấu hình hiện tại trỏ đến release client-v1.0.0 trên GitHub:

    https://github.com/echne/tlbb-setup-storage/releases/download/client-v1.0.0/manifest.json

Launcher chỉ chấp nhận URL HTTPS và redirect đến host có trong danh sách trusted host. Với GitHub Releases, cấu hình hiện cho phép github.com và release-assets.githubusercontent.com. Không tắt xác minh chứng chỉ TLS để khắc phục lỗi kết nối.

Cấu trúc manifest được mô tả trong src\TLBB.Launcher\Config\manifest.schema.json; manifest.sample.json là mẫu chưa điền dữ liệu production. manifest.json trong thư mục Config là bản metadata hiện có trong source; khi chạy, launcher lấy manifest từ URL trong appsettings.json, không cài từ file JSON local.

Manifest cần có:

- Phiên bản schema, tên game, phiên bản client và đường dẫn executable sau khi giải nén.
- URL đăng ký nếu có; hiện trường này chưa được cấu hình.
- Archive định dạng split-zip, tên volume ZIP cuối, tổng kích thước sau giải nén.
- Danh sách volume, mỗi volume gồm tên file, URL HTTPS, kích thước byte và SHA-256 64 ký tự.

Client hiện khai báo Launch.exe và bộ split ZIP gồm TLBB.z01 đến TLBB.z07 cùng TLBB.zip. Khi phát hành phiên bản mới, phải đồng bộ file trên CDN/GitHub với manifest: tính lại kích thước và SHA-256 từ đúng file sẽ phân phối, rồi cập nhật manifest. Không đưa mật khẩu, khóa riêng hay thông tin đăng nhập R2 vào launcher hoặc manifest.

## Luồng cài đặt và dữ liệu

1. Launcher đọc manifest từ endpoint cấu hình và kiểm tra dữ liệu trước khi cho cài.
2. Khi nhấn Bắt đầu cài đặt, launcher tạo thư mục game. Nếu đã chọn loại trừ Defender, launcher yêu cầu UAC để thêm đúng thư mục này trước khi tải.
3. Các volume tải xuống được lưu trong thư mục con .staging của thư mục cài game. File tải dở cùng metadata được giữ lại để có thể thử tiếp.
4. Sau khi tải xong, launcher xác minh kích thước và SHA-256 của từng volume, giải nén vào vùng staging riêng, kiểm tra executable và hoàn tất commit.
5. Nếu được chọn, shortcut Desktop trỏ đến Launch.exe được tạo. Staging được dọn sau khi hoàn tất thành công.

Thông tin lựa chọn cài đặt và trạng thái cần thiết được lưu dưới %LOCALAPPDATA%\TLBBPhongVanKiem. Mỗi lần mở ứng dụng vẫn hiển thị phiên mới ở bước đầu; trạng thái này không tự đưa giao diện trở lại bước Hoàn tất.

### Lưu ý về Microsoft Defender

Thêm thư mục vào danh sách loại trừ làm giảm khả năng quét các file bên trong thư mục đó. Chỉ dùng với client lấy từ nguồn mà bạn tin cậy. Launcher không tự tắt Defender và không thêm loại trừ process hay extension. Thao tác thêm thư mục dùng yêu cầu UAC riêng; nếu Windows, Group Policy, Intune hoặc Tamper Protection từ chối, launcher sẽ báo lỗi.

Nếu máy có antivirus khác, launcher hiển thị hướng dẫn tương ứng và khóa tùy chọn loại trừ Defender.

## Cấu trúc source

- src\TLBB.Launcher — ứng dụng WPF, giao diện, ViewModel, manifest, tải xuống, xác minh hash, archive, cài đặt, Defender và chạy game.
- src\TLBB.Launcher\Config — cấu hình endpoint, manifest hiện tại, manifest mẫu và JSON Schema.
- src\TLBB.Launcher\Assets — logo, ảnh nền và icon EXE.
- tests\TLBB.Launcher.Tests — unit và integration tests.
- docs\TECHNICAL-DEBT.md — các hạng mục cần hoàn tất trước khi phát hành rộng.
- publish-win-x64.ps1 — publish self-contained một file EXE và kiểm tra đầu ra.

## Xử lý lỗi manifest/TLS

Nếu một máy báo không thể thiết lập kết nối SSL trong khi trình duyệt truy cập được URL, hãy xem phần chi tiết kỹ thuật trong hộp thoại lỗi. Launcher dùng TLS và xác minh chứng chỉ của Windows; trình duyệt có thể dùng kho chứng chỉ hoặc proxy khác. Có thể kiểm tra ngày giờ hệ thống, cập nhật Windows, proxy và tính năng quét HTTPS của phần mềm bảo mật. Không vô hiệu hóa xác minh chứng chỉ hoặc thêm mọi host làm host tin cậy để né lỗi.

## Việc còn lại trước khi phát hành

- Cung cấp URL đăng ký nếu muốn hiện nút đăng ký.
- Hoàn thành các kiểm tra thủ công về cài đặt, rollback, shortcut, tiếp tục tải và lỗi mạng trên Windows 10/11.
- Cấu hình log phù hợp cho vận hành và hỗ trợ người chơi.
- Ký số bản phát hành khi có chứng thư ký.
- Đảm bảo manifest trên máy chủ luôn khớp với bộ archive sẽ phát hành.

Xem danh sách chi tiết tại [docs/TECHNICAL-DEBT.md](docs/TECHNICAL-DEBT.md).
