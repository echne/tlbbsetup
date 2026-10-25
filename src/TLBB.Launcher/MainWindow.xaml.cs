using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Security.Principal;
using System.Windows.Media;
using TLBB.Launcher.Services;
using TLBB.Launcher.ViewModels;

namespace TLBB.Launcher;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ErrorMessageRequested += ShowErrorMessage;
        viewModel.CloseRequested += Close;
        viewModel.AdministratorRestartRequested += AskToRestartAsAdministrator;
    }

    private void ShowErrorMessage(string title, string message)
    {
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void AskToRestartAsAdministrator()
    {
        if (IsRunningAsAdministrator())
        {
            ShowErrorMessage(
                "Không thể ghi vào thư mục",
                "Setup đã chạy với quyền quản trị viên nhưng vẫn không ghi được vào thư mục này. Hãy chọn thư mục cài đặt khác hoặc kiểm tra quyền truy cập.");
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Setup không có quyền ghi vào thư mục cài đặt đã chọn.\n\nBạn có muốn mở lại setup với quyền quản trị viên không?",
            "Cần quyền quản trị viên",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await ViewModel.SaveSettingsAsync();
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                throw new InvalidOperationException("Không xác định được file setup đang chạy.");
            }

            var elevatedProcess = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                Verb = "runas"
            });
            if (elevatedProcess is null)
            {
                throw new InvalidOperationException("Windows chưa khởi chạy được setup với quyền quản trị viên.");
            }

            Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            MessageBox.Show(
                this,
                "Yêu cầu quyền quản trị đã bị hủy. Setup hiện tại vẫn mở; bạn có thể chọn thư mục khác hoặc thử lại.",
                "Yêu cầu đã bị hủy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowErrorMessage("Không thể mở lại setup", $"Windows không thể mở setup với quyền quản trị viên.\n\nChi tiết: {ex.Message}");
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void WindowFrame_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement frame && frame.ActualWidth > 0 && frame.ActualHeight > 0)
        {
            frame.Clip = new RectangleGeometry(new Rect(0, 0, frame.ActualWidth, frame.ActualHeight), 12, 12);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed ||
            e.ClickCount > 1 || IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        DragMove();
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button) return true;
            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục cài đặt TLBB Phong Vân Kiếm",
            InitialDirectory = Directory.Exists(ViewModel.InstallPath)
                ? ViewModel.InstallPath
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.InstallPath = InstallPathPolicy.EnsureGameDirectory(dialog.FolderName);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Bạn có muốn hủy tác vụ và thoát setup không? Các phần đã tải hoàn tất sẽ được giữ lại để tiếp tục lần sau.",
            "Hủy và thoát setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            ViewModel.CancelCurrentOperation();
        }
    }
}
