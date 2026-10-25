using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
using TLBB.Launcher.Infrastructure;
using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IManifestService _manifestService;
    private readonly IDownloadService _downloadService;
    private readonly IInstallationService _installationService;
    private readonly IDiskSpaceService _diskSpaceService;
    private readonly ISettingsService _settingsService;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IWindowsDefenderExclusionService _defenderExclusionService;
    private readonly IAntivirusStatusService _antivirusStatusService;
    private readonly LauncherStateMachine _stateMachine = new();
    private readonly OverallProgressTracker _progressTracker = new();
    private readonly Stopwatch _speedClock = Stopwatch.StartNew();
    private CancellationTokenSource? _operationCancellation;
    private LauncherManifest? _manifest;
    private InstallationState? _savedState;
    private string _installPath = @"C:\Games\TLBBPhongVanKiem";
    private bool _createShortcut = true;
    private bool _createDefenderExclusion;
    private bool _isApplyingDefenderExclusion;
    private bool _isBusy;
    private bool _isPaused;
    private bool _pauseRequested;
    private bool _exitAfterCancellation;
    private bool _isAntivirusStatusKnown;
    private AntivirusStatus? _antivirusStatus;
    private Task? _antivirusStatusTask;
    private int _selectedStep;
    private string _statusMessage = "Đang nạp cấu hình trình cài đặt...";
    private string _currentFile = "Chưa có dữ liệu";
    private string _downloadDetails = "Đang chuẩn bị...";
    private string _speedText = "—";
    private string _antivirusHint = string.Empty;
    private OverallProgress _overallProgress = OverallProgress.Initial;
    private DiskSpaceReport _diskReport = new(false, "Đang tải manifest để kiểm tra điều kiện cài đặt.", 0, 0);
    private long _lastSpeedBytes;

    public MainViewModel(
        IManifestService manifestService,
        IDownloadService downloadService,
        IInstallationService installationService,
        IDiskSpaceService diskSpaceService,
        ISettingsService settingsService,
        IGameLaunchService gameLaunchService,
        IWindowsDefenderExclusionService defenderExclusionService,
        IAntivirusStatusService antivirusStatusService)
    {
        _manifestService = manifestService;
        _downloadService = downloadService;
        _installationService = installationService;
        _diskSpaceService = diskSpaceService;
        _settingsService = settingsService;
        _gameLaunchService = gameLaunchService;
        _defenderExclusionService = defenderExclusionService;
        _antivirusStatusService = antivirusStatusService;

        StartCommand = new AsyncRelayCommand(StartInstallationAsync, () => CanStart);
        PauseCommand = new RelayCommand(Pause, () => IsBusy && !_pauseRequested);
        ResumeCommand = new AsyncRelayCommand(StartInstallationAsync, () => IsPaused && !IsBusy);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => IsBusy || IsPaused);
        PlayCommand = new RelayCommand(Play, () => IsComplete && _manifest is not null);
        RegisterCommand = new RelayCommand(Register, () => IsComplete && _manifest?.RegisterUrl is not null && _manifestService.IsTrustedHttpsUrl(_manifest.RegisterUrl));
    }

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand RegisterCommand { get; }
    public event Action<string, string>? ErrorMessageRequested;
    public event Action? CloseRequested;
    public event Action? AdministratorRestartRequested;

    public string InstallPath
    {
        get => _installPath;
        set
        {
            if (SetProperty(ref _installPath, value))
            {
                UpdateDiskReport();
                SaveSettingsInBackground();
                OnPropertyChanged(nameof(CanAddDefenderExclusion));
                RefreshCommands();
            }
        }
    }

    public bool CreateShortcut
    {
        get => _createShortcut;
        set
        {
            if (SetProperty(ref _createShortcut, value)) SaveSettingsInBackground();
        }
    }

    public bool CreateDefenderExclusion
    {
        get => _createDefenderExclusion;
        set
        {
            if (value && !CanAddDefenderExclusion) return;
            if (!SetProperty(ref _createDefenderExclusion, value)) return;
            SaveSettingsInBackground();
            OnPropertyChanged(nameof(ShowAntivirusHint));
        }
    }

    public bool IsApplyingDefenderExclusion
    {
        get => _isApplyingDefenderExclusion;
        private set
        {
            if (SetProperty(ref _isApplyingDefenderExclusion, value))
            {
                OnPropertyChanged(nameof(CanAddDefenderExclusion));
                OnPropertyChanged(nameof(ShowPauseButton));
                OnPropertyChanged(nameof(ShowCancelButton));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ShowPauseButton));
                OnPropertyChanged(nameof(ShowCancelButton));
                RefreshCommands();
            }
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(ShowResumeButton));
                OnPropertyChanged(nameof(ShowCancelButton));
                RefreshCommands();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentFile
    {
        get => _currentFile;
        private set => SetProperty(ref _currentFile, value);
    }

    public string DownloadDetails
    {
        get => _downloadDetails;
        private set => SetProperty(ref _downloadDetails, value);
    }

    public string SpeedText
    {
        get => _speedText;
        private set => SetProperty(ref _speedText, value);
    }

    public string AntivirusHint
    {
        get => _antivirusHint;
        private set
        {
            if (SetProperty(ref _antivirusHint, value))
            {
                OnPropertyChanged(nameof(ShowAntivirusHint));
            }
        }
    }

    public OverallProgress OverallProgress
    {
        get => _overallProgress;
        private set
        {
            if (SetProperty(ref _overallProgress, value))
            {
                OnPropertyChanged(nameof(ProgressPercentText));
            }
        }
    }

    public DiskSpaceReport DiskReport
    {
        get => _diskReport;
        private set
        {
            if (SetProperty(ref _diskReport, value))
            {
                OnPropertyChanged(nameof(CanStart));
                RefreshCommands();
            }
        }
    }

    public int SelectedStep
    {
        get => _selectedStep;
        private set
        {
            if (SetProperty(ref _selectedStep, value))
            {
                OnPropertyChanged(nameof(IsSetupVisible));
                OnPropertyChanged(nameof(IsDownloadVisible));
                OnPropertyChanged(nameof(IsInstallVisible));
                OnPropertyChanged(nameof(IsCompleteVisible));
                OnPropertyChanged(nameof(IsStep1Active));
                OnPropertyChanged(nameof(IsStep1Done));
                OnPropertyChanged(nameof(IsStep2Active));
                OnPropertyChanged(nameof(IsStep3Active));
                OnPropertyChanged(nameof(IsStep4Active));
                OnPropertyChanged(nameof(IsStep2Done));
                OnPropertyChanged(nameof(IsStep3Done));
                OnPropertyChanged(nameof(IsStep4Done));
                OnPropertyChanged(nameof(ShowPrimaryButton));
                OnPropertyChanged(nameof(ShowPlayButton));
                OnPropertyChanged(nameof(ShowRegistrationButton));
                OnPropertyChanged(nameof(RegistrationAvailable));
                RefreshCommands();
            }
        }
    }

    public string GameVersion => _manifest?.ClientVersion ?? "Chưa cấu hình";
    public string ProgressPercentText => $"{OverallProgress.Percentage}%";
    public bool IsSetupVisible => SelectedStep == 0;
    public bool IsDownloadVisible => SelectedStep == 1;
    public bool IsInstallVisible => SelectedStep == 2;
    public bool IsCompleteVisible => SelectedStep == 3;
    public bool IsStep1Active => SelectedStep == 0;
    public bool IsStep1Done => SelectedStep > 0;
    public bool IsStep2Active => SelectedStep == 1;
    public bool IsStep3Active => SelectedStep == 2;
    public bool IsStep4Active => SelectedStep == 3;
    public bool IsStep2Done => SelectedStep > 1;
    public bool IsStep3Done => SelectedStep > 2;
    public bool IsStep4Done => IsComplete;
    public bool IsComplete => SelectedStep == 3 && _stateMachine.State == LauncherWorkflowState.Complete;
    public bool CanStart => !IsBusy && !IsPaused && SelectedStep == 0 && _manifest is not null;
    public bool CanAddDefenderExclusion => _isAntivirusStatusKnown && !IsApplyingDefenderExclusion &&
        _antivirusStatus is { DetectionAvailable: true, DefenderActive: true, OtherRegisteredProducts.Count: 0 };
    public bool ShowAntivirusHint => _isAntivirusStatusKnown && !string.IsNullOrWhiteSpace(AntivirusHint);
    public bool RegistrationAvailable => _manifest?.RegisterUrl is not null && _manifestService.IsTrustedHttpsUrl(_manifest.RegisterUrl);
    public bool ShowPrimaryButton => SelectedStep == 0;
    public bool ShowPlayButton => IsComplete;
    public bool ShowRegistrationButton => IsComplete && RegistrationAvailable;
    public bool ShowPauseButton => IsBusy && !_pauseRequested && !IsApplyingDefenderExclusion;
    public bool ShowResumeButton => IsPaused;
    public bool ShowCancelButton => (IsBusy || IsPaused) && !IsApplyingDefenderExclusion;
    public string CompletionHeading => "Cài đặt hoàn tất!";
    public string CompletionDescription => "Chúc đại hiệp có một hành trình\nhành tẩu giang hồ thật vui vẻ!";
    public string PrimaryButtonText => _savedState is { Status: "Paused" or "Downloading" } ? "Tiếp tục cài đặt →" : "Bắt đầu cài đặt →";

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadAsync();
        _installPath = settings.InstallPath;
        _createShortcut = settings.CreateDesktopShortcut;
        _createDefenderExclusion = settings.CreateDefenderExclusion;
        OnPropertyChanged(nameof(InstallPath));
        OnPropertyChanged(nameof(CreateShortcut));
        OnPropertyChanged(nameof(CreateDefenderExclusion));
        OnPropertyChanged(nameof(CanAddDefenderExclusion));
        _antivirusStatusTask = RefreshAntivirusStatusAsync();

        await RefreshManifestAsync();

        UpdateDiskReport();
        RefreshCommands();
    }

    public async Task RefreshManifestAsync()
    {
        try
        {
            _manifest = await _manifestService.LoadAsync();
            StatusMessage = $"Đã nạp manifest phiên bản {_manifest.ClientVersion}.";
        }
        catch (Exception ex) when (ex is HttpRequestException or LauncherConfigurationException or InvalidDataException or JsonException)
        {
            _manifest = null;
            if (ex is HttpRequestException)
            {
                StatusMessage = "Không thể kết nối an toàn tới máy chủ manifest.";
                ErrorMessageRequested?.Invoke(
                    "Không thể nạp manifest",
                    $"Launcher không kết nối được an toàn tới máy chủ manifest. Hãy kiểm tra ngày giờ Windows và cập nhật Windows. Nếu máy đang dùng proxy hoặc phần mềm bảo mật kiểm tra HTTPS, kết nối có thể bị ảnh hưởng.\n\nChi tiết kỹ thuật:\n{FormatExceptionChain(ex)}");
            }
            else
            {
                StatusMessage = ex is LauncherConfigurationException
                    ? "Nguồn tải chưa được cấu hình. BQT cần cung cấp URL manifest HTTPS và host tin cậy."
                    : $"Không thể nạp manifest: {ex.Message}";
            }
        }

        OnPropertyChanged(nameof(GameVersion));
        OnPropertyChanged(nameof(RegistrationAvailable));
        OnPropertyChanged(nameof(ShowRegistrationButton));
        UpdateDiskReport();
        RefreshCommands();
    }

    private async Task<string> ApplyDefenderExclusionAsync(string installDirectory)
    {
        if (!DefenderExclusionPathPolicy.TryNormalizeFolder(installDirectory, out var folderPath, out var error))
        {
            throw new InvalidOperationException(error);
        }

        IsApplyingDefenderExclusion = true;
        StatusMessage = "Đang chờ xác nhận quyền Administrator để thêm loại trừ Defender.";
        try
        {
            var result = await _defenderExclusionService.AddFolderExclusionAsync(folderPath);
            return result == DefenderExclusionResult.AlreadyPresent
                ? $"Thư mục {folderPath} đã có trong danh sách loại trừ Defender."
                : $"Đã thêm đúng thư mục {folderPath} vào loại trừ Defender.";
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("Bạn đã hủy xác nhận UAC nên Defender chưa được thay đổi. Hãy đồng ý yêu cầu quyền hoặc bỏ chọn tùy chọn loại trừ rồi thử lại.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không thể thêm thư mục vào loại trừ Defender: {ex.Message}", ex);
        }
        finally
        {
            IsApplyingDefenderExclusion = false;
        }
    }

    public void Pause()
    {
        if (!IsBusy) return;
        _pauseRequested = true;
        StatusMessage = "Đang tạm dừng an toàn; dữ liệu tải dở sẽ được giữ lại.";
        _operationCancellation?.Cancel();
        ((RelayCommand)PauseCommand).NotifyCanExecuteChanged();
    }

    public void CancelCurrentOperation()
    {
        if (IsBusy)
        {
            _exitAfterCancellation = true;
            _pauseRequested = false;
            StatusMessage = "Đang hủy tác vụ; các volume đã tải và xác minh sẽ được giữ lại.";
            _operationCancellation?.Cancel();
            return;
        }

        if (IsPaused)
        {
            _stateMachine.TryTransition(LauncherWorkflowState.Setup);
            IsPaused = false;
            SelectedStep = 0;
            StatusMessage = "Đã hủy phiên tải. Có thể tiếp tục cài đặt sau.";
            CloseRequested?.Invoke();
        }
    }

    private async Task StartInstallationAsync()
    {
        if (_antivirusStatusTask is not null)
        {
            await _antivirusStatusTask;
            _antivirusStatusTask = null;
        }

        if (_manifest is null)
        {
            await RefreshManifestAsync();
            if (_manifest is null) return;
        }

        UpdateDiskReport();
        string installDirectory;
        try
        {
            if (string.IsNullOrWhiteSpace(InstallPath) || !Path.IsPathFullyQualified(InstallPath.Trim()))
            {
                throw new ArgumentException("Nhập đường dẫn thư mục cài đặt Windows đầy đủ, ví dụ D:\\Games\\TLBBPhongVanKiem.");
            }
            installDirectory = Path.GetFullPath(InstallPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            var reason = DiskReport.IsValid ? ex.Message : DiskReport.Message;
            ShowInstallError("Đường dẫn cài đặt chưa hợp lệ", reason);
            return;
        }

        var pendingCommit = _installationService.HasPendingCommit(installDirectory, _manifest.ClientVersion);
        if (!pendingCommit && !DiskReport.IsValid)
        {
            if (DiskReport.RequiresAdministrator)
            {
                AdministratorRestartRequested?.Invoke();
            }
            else
            {
                ShowInstallError("Chưa thể bắt đầu cài đặt", DiskReport.Message);
            }
            return;
        }

        var addDefenderExclusion = CreateDefenderExclusion;
        if (addDefenderExclusion &&
            !DefenderExclusionPathPolicy.TryNormalizeFolder(installDirectory, out _, out var defenderPathError))
        {
            ShowInstallError("Không thể thêm loại trừ Defender", defenderPathError);
            return;
        }

        if (!_stateMachine.TryTransition(LauncherWorkflowState.Downloading))
        {
            return;
        }

        var createDesktopShortcut = CreateShortcut;
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _pauseRequested = false;
        _exitAfterCancellation = false;
        IsPaused = false;
        IsBusy = true;
        SelectedStep = pendingCommit ? 2 : 1;
        StatusMessage = pendingCommit ? "Đang hoàn tất commit và dọn dữ liệu tạm đã lưu." : "Đang tải các volume từ nguồn HTTPS đã xác minh.";
        CurrentFile = pendingCommit ? "Đang tiếp tục hoàn tất cài đặt" : "Đang chuẩn bị tải xuống";
        _speedClock.Restart();
        _lastSpeedBytes = 0;
        var cancellationToken = _operationCancellation.Token;
        string? defenderStatus = null;

        try
        {
            Directory.CreateDirectory(installDirectory);
            if (addDefenderExclusion)
            {
                defenderStatus = await ApplyDefenderExclusionAsync(installDirectory);
            }

            var stagingDirectory = Path.Combine(installDirectory, ".staging");
            if (!pendingCommit)
            {
                if (File.Exists(stagingDirectory))
                {
                    throw new IOException("Đường dẫn .staging đang là một tệp. Hãy xóa hoặc đổi tên tệp đó rồi thử lại.");
                }
                if (Directory.Exists(stagingDirectory) &&
                    (File.GetAttributes(stagingDirectory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Thư mục .staging đang trỏ tới một vị trí khác. Hãy xóa liên kết đó rồi thử lại.");
                }
                Directory.CreateDirectory(stagingDirectory);
            }
            await _settingsService.SaveAsync(new LauncherSettings
            {
                InstallPath = installDirectory,
                CreateDesktopShortcut = createDesktopShortcut,
                CreateDefenderExclusion = addDefenderExclusion
            });

            IReadOnlyList<string> partPaths;
            if (pendingCommit)
            {
                partPaths = [];
                await PersistStateAsync("Installing");
            }
            else
            {
                var downloadProgress = new Progress<DownloadProgress>(value =>
                {
                    OverallProgress = _progressTracker.ReportDownload(value.Fraction, value.Message);
                    CurrentFile = "Đang tải các phần dữ liệu game";
                    DownloadDetails = $"{FormatBytes(Math.Min(value.BytesReceived, value.TotalBytes))} / {FormatBytes(value.TotalBytes)} · Đang tải dữ liệu game";
                    var elapsed = Math.Max(0.001, _speedClock.Elapsed.TotalSeconds);
                    var bytesPerSecond = Math.Max(0, value.BytesReceived - _lastSpeedBytes) / elapsed;
                    SpeedText = FormatRate(bytesPerSecond);
                    _lastSpeedBytes = value.BytesReceived;
                    _speedClock.Restart();
                });

                partPaths = await _downloadService.DownloadAndVerifyAsync(_manifest, stagingDirectory, downloadProgress, cancellationToken);
                OverallProgress = _progressTracker.ReportDownload(1, "Đã xác minh toàn bộ volume tải xuống");
                await PersistStateAsync("Installing", partPaths.Select(Path.GetFileName).Where(name => name is not null).Cast<string>().ToArray());
            }

            if (!_stateMachine.TryTransition(LauncherWorkflowState.Installing))
            {
                throw new InvalidOperationException("Không thể chuyển sang bước cài đặt.");
            }

            SelectedStep = 2;
            StatusMessage = "Đang xác minh gói, giải nén và hoàn thiện file game.";
            var installProgress = new Progress<PhaseProgress>(value =>
            {
                OverallProgress = _progressTracker.ReportPhase(value);
                CurrentFile = value.CurrentFile ?? value.Message;
                StatusMessage = value.Message;
            });
            await _installationService.InstallAsync(_manifest, partPaths, installDirectory, createDesktopShortcut, installProgress, cancellationToken);

            OverallProgress = _progressTracker.ReportPhase(new PhaseProgress(ProgressPhase.Complete, 1, "Cài đặt và dọn dẹp hoàn tất"));
            await PersistStateAsync("Complete", _manifest.Archive.Parts.Select(part => part.Name).ToArray());
            _stateMachine.TryTransition(LauncherWorkflowState.Complete);
            SelectedStep = 3;
            StatusMessage = defenderStatus is null
                ? "Đã xác minh file game và dọn dữ liệu tạm thành công."
                : $"Đã cài đặt game thành công. {defenderStatus}";
            CurrentFile = _manifest.EntryExecutable;
            SpeedText = "Hoàn tất";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (_pauseRequested)
            {
                _stateMachine.TryTransition(LauncherWorkflowState.Paused);
                IsPaused = true;
                StatusMessage = "Đã tạm dừng. Nhấn tiếp tục để tải phần còn lại và hoàn tất cài đặt.";
                await PersistStateAsync("Paused");
            }
            else
            {
                _stateMachine.TryTransition(LauncherWorkflowState.Setup);
                SelectedStep = 0;
                StatusMessage = "Đã hủy phiên cài đặt; dữ liệu đã tải được giữ lại để thử lại.";
                await PersistStateAsync("Paused");
            }
        }
        catch (Exception ex)
        {
            _stateMachine.TryTransition(LauncherWorkflowState.Failed);
            SelectedStep = 0;
            StatusMessage = "Có thể thử lại sau khi xử lý thông báo lỗi.";
            ErrorMessageRequested?.Invoke("Cài đặt chưa hoàn tất", FormatFriendlyError(ex));
            await PersistStateAsync("Failed");
        }
        finally
        {
            var exitAfterCancellation = _exitAfterCancellation;
            _exitAfterCancellation = false;
            IsBusy = false;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            _pauseRequested = false;
            RefreshCommands();
            UpdateDiskReport();
            if (exitAfterCancellation)
            {
                CloseRequested?.Invoke();
            }
        }
    }

    private void Play()
    {
        if (_manifest is null || !IsComplete) return;
        try
        {
            _gameLaunchService.Launch(InstallPath, _manifest.EntryExecutable);
            CloseRequested?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể khởi chạy game: {ex.Message}";
        }
    }

    private void Register()
    {
        if (_manifest?.RegisterUrl is null || !_manifestService.IsTrustedHttpsUrl(_manifest.RegisterUrl))
        {
            StatusMessage = "URL đăng ký HTTPS chưa được cấu hình trong manifest.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_manifest.RegisterUrl.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không thể mở trang đăng ký: {ex.Message}";
        }
    }

    private void UpdateDiskReport()
    {
        DiskReport = _manifest is null
            ? new DiskSpaceReport(false, "Chưa thể kiểm tra dung lượng vì manifest chưa được cấu hình.", 0, 0)
            : _diskSpaceService.ValidateInstallTarget(InstallPath, _manifest, GetDownloadStagingPath(InstallPath));
        OnPropertyChanged(nameof(CanStart));
    }

    private void ShowInstallError(string title, string message)
    {
        StatusMessage = "Có thể sửa thiết lập rồi thử lại.";
        ErrorMessageRequested?.Invoke(title, message);
    }

    private async Task RefreshAntivirusStatusAsync()
    {
        AntivirusStatus status;
        try
        {
            status = await _antivirusStatusService.GetStatusAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            status = new AntivirusStatus(false, false, []);
        }

        _antivirusStatus = status;
        _isAntivirusStatusKnown = true;
        AntivirusHint = status.GetFriendlyWarning() ?? string.Empty;
        OnPropertyChanged(nameof(CanAddDefenderExclusion));
        OnPropertyChanged(nameof(ShowAntivirusHint));

        if (CreateDefenderExclusion && !CanAddDefenderExclusion)
        {
            _createDefenderExclusion = false;
            OnPropertyChanged(nameof(CreateDefenderExclusion));
            SaveSettingsInBackground();
        }
    }

    private static string GetDownloadStagingPath(string installPath)
    {
        try
        {
            return Path.Combine(Path.GetFullPath(installPath.Trim()), ".staging");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return installPath;
        }
    }

    private static string FormatFriendlyError(Exception exception)
    {
        var error = exception.GetBaseException();
        var details = error.Message;
        return error switch
        {
            UnauthorizedAccessException => $"Windows không cho phép ghi vào thư mục cài đặt hoặc .staging. Hãy chọn thư mục khác hoặc kiểm tra quyền truy cập.\n\nChi tiết: {details}",
            IOException when details.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) =>
                $"Windows hoặc phần mềm bảo mật đang chặn truy cập vào file cài đặt tạm. Hãy xác nhận loại trừ Defender đã được thêm, rồi thử lại.\n\nChi tiết: {details}",
            HttpRequestException => $"Không thể kết nối tới máy chủ tải game. Kiểm tra Internet rồi thử lại.\n\nChi tiết: {details}",
            InvalidDataException => $"Dữ liệu tải về hoặc gói cài đặt không hợp lệ. Hãy thử cài đặt lại.\n\nChi tiết: {details}",
            _ => $"{details}"
        };
    }

    private static string FormatExceptionChain(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var message = $"{current.GetType().Name}: {current.Message}";
            if (!messages.Contains(message, StringComparer.Ordinal))
            {
                messages.Add(message);
            }
        }

        return string.Join(Environment.NewLine + "→ ", messages);
    }

    private void SaveSettingsInBackground()
    {
        _ = SaveSettingsAsync();
    }

    public Task SaveSettingsAsync() => _settingsService.SaveAsync(new LauncherSettings
    {
        InstallPath = InstallPath,
        CreateDesktopShortcut = CreateShortcut,
        CreateDefenderExclusion = CreateDefenderExclusion
    });

    private Task PersistStateAsync(string status, IReadOnlyList<string>? verifiedParts = null)
    {
        if (_manifest is null) return Task.CompletedTask;
        _savedState = new InstallationState
        {
            ClientVersion = _manifest.ClientVersion,
            TargetDirectory = InstallPath,
            Status = status,
            LastOverallProgress = OverallProgress.Percentage,
            VerifiedParts = verifiedParts ?? _savedState?.VerifiedParts ?? []
        };
        OnPropertyChanged(nameof(PrimaryButtonText));
        return _settingsService.SaveInstallationStateAsync(_savedState);
    }

    private void RefreshCommands()
    {
        (StartCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (ResumeCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (PauseCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CancelCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PlayCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RegisterCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private static string FormatRate(double bytesPerSecond) => bytesPerSecond switch
    {
        >= 1024 * 1024 => $"{bytesPerSecond / 1024 / 1024:0.0} MB/s",
        >= 1024 => $"{bytesPerSecond / 1024:0} KB/s",
        _ => $"{bytesPerSecond:0} B/s"
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024 / 1024:0.00} GB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B"
    };
}
