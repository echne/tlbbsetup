namespace TLBB.Launcher.Models;

public sealed record AntivirusStatus(
    bool DetectionAvailable,
    bool DefenderActive,
    IReadOnlyList<string> OtherRegisteredProducts)
{
    public string? GetFriendlyWarning()
    {
        if (OtherRegisteredProducts.Count > 0)
        {
            var product = OtherRegisteredProducts[0];
            return $"Đang dùng {product}; hãy thêm thư mục game vào loại trừ của {product}.";
        }

        if (!DetectionAvailable)
        {
            return "Chưa xác định được trạng thái Windows Defender nên tùy chọn này đang bị khóa.";
        }

        if (!DefenderActive)
        {
            return "Windows Defender không bảo vệ chủ động nên tùy chọn này đang bị khóa.";
        }

        return null;
    }
}
