using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using echo.Abstractions.Engines;
using echo.Abstractions.Platform;
using echo.App.Localization;
using echo.App.Services;
using echo.App.ViewModels;
using echo.App.Views;
using echo.Core;
using Microsoft.Extensions.Logging.Abstractions;

namespace echo.App.Tests;

/// <summary>
/// Regression for v1.9.25: picking another engine with device = CPU snapped back to the previous
/// engine because the device ComboBox re-selection (null → cpu) re-filtered EngineOptions while the
/// engine ComboBox was still committing the user's selection.
/// </summary>
public sealed class SettingsEngineSwitchTests : IDisposable
{
    private static readonly string[] AllEngines = ["gigaam", "whisper", "omnilingual", "parakeet_npu"];
    private readonly string _configRoot;
    private readonly string? _previousXdg;

    public SettingsEngineSwitchTests()
    {
        _previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _configRoot = Path.Combine(Path.GetTempPath(), "echo-app-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _configRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousXdg);
        try
        {
            Directory.Delete(_configRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [AvaloniaTheory]
    [InlineData("gigaam")]
    [InlineData("whisper")]
    [InlineData("omnilingual")]
    public void Cpu_SwitchFromParakeet_KeepsSelectedEngine(string target)
    {
        var (vm, window) = CreateSettings(engine: "parakeet_npu", device: ExecutionProviderResolver.CpuDevice);
        try
        {
            Assert.Equal("parakeet_npu", vm.Engine);
            var engineCombo = FindEngineCombo(window);

            SelectEngine(engineCombo, target);

            Assert.Equal(target, vm.Engine);
            Assert.Equal(target, vm.SelectedEngine?.Id);
            Assert.Equal(target, (engineCombo.SelectedItem as EngineOption)?.Id);
            Assert.Equal(ExecutionProviderResolver.CpuDevice, vm.SelectedComputeDevice?.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Cpu_SwitchBetweenNonParakeetEngines_KeepsEachSelection()
    {
        var (vm, window) = CreateSettings(engine: "gigaam", device: ExecutionProviderResolver.CpuDevice);
        try
        {
            var engineCombo = FindEngineCombo(window);
            foreach (var target in new[] { "omnilingual", "parakeet_npu", "gigaam" })
            {
                SelectEngine(engineCombo, target);
                Assert.Equal(target, vm.Engine);
                Assert.Equal(target, (engineCombo.SelectedItem as EngineOption)?.Id);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Npu_ThenCpu_ThenGigaAm_SwitchesEngine()
    {
        var (vm, window) = CreateSettings(engine: "parakeet_npu", device: ExecutionProviderResolver.NpuDevice);
        try
        {
            Assert.Equal(ExecutionProviderResolver.NpuDevice, vm.SelectedComputeDevice?.Id);
            Assert.Equal(["parakeet_npu"], vm.EngineOptions.Select(o => o.Id));

            var deviceCombo = window.GetVisualDescendants().OfType<ComboBox>()
                .First(c => c.ItemsSource is IReadOnlyList<ComputeDeviceOption>);
            deviceCombo.SelectedIndex = vm.ComputeDeviceOptions
                .Select((o, i) => (o, i))
                .First(x => x.o.Id == ExecutionProviderResolver.CpuDevice).i;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ExecutionProviderResolver.CpuDevice, vm.SelectedComputeDevice?.Id);
            Assert.Equal(AllEngines, vm.EngineOptions.Select(o => o.Id));
            Assert.Equal("parakeet_npu", vm.Engine);

            var engineCombo = FindEngineCombo(window);
            SelectEngine(engineCombo, "gigaam");

            Assert.Equal("gigaam", vm.Engine);
            Assert.Equal("gigaam", (engineCombo.SelectedItem as EngineOption)?.Id);
        }
        finally
        {
            window.Close();
        }
    }

    private static void SelectEngine(ComboBox combo, string engineId)
    {
        var options = combo.ItemsSource!.Cast<EngineOption>().ToList();
        combo.SelectedIndex = options.FindIndex(o => o.Id == engineId);
        Dispatcher.UIThread.RunJobs();
    }

    private static ComboBox FindEngineCombo(Window window) =>
        window.GetVisualDescendants().OfType<ComboBox>()
            .First(c => c.ItemsSource is IReadOnlyList<EngineOption>);

    private static (SettingsViewModel Vm, Window Window) CreateSettings(string engine, string device)
    {
        var configStore = new ConfigStore(NullLogger<ConfigStore>.Instance);
        var seed = new AppConfig { Engine = engine, Device = device };
        configStore.Save(seed);

        var npu = Fake<INpuAvailability>.Create(new()
        {
            ["IsLikelyPlatform"] = true,
            ["IsHardwareDetected"] = true,
            ["IsAvailable"] = true,
            ["StatusDetail"] = "test",
        });
        var registry = Fake<ITranscriptionEngineRegistry>.Create(new()
        {
            ["RegisteredEngineIds"] = (IReadOnlyCollection<string>)AllEngines,
        });
        var audio = Fake<IAudioCapture>.Create();
        var hotkey = Fake<IHotkeyService>.Create();

        var coordinator = new DictationCoordinator(
            configStore,
            new TranscriptionService(registry),
            new HistoryStore(),
            audio,
            hotkey,
            Fake<ITextInjector>.Create(),
            Fake<IFocusTarget>.Create(),
            Fake<ITrayStateService>.Create(),
            Fake<IDictationResultNotifier>.Create(),
            NullLogger<DictationCoordinator>.Instance);

        var loc = new LocalizationService();
        loc.Apply("en");
        var status = new AppStatusViewModel(configStore, loc);
        var applyService = new SettingsApplyService(coordinator, status);
        var home = new HomeViewModel(coordinator, loc);
        var models = new ModelSettingsController(
            new ModelDownloader(NullLogger<ModelDownloader>.Instance, new HttpClient()),
            coordinator,
            applyService,
            status,
            home,
            loc);

        var vm = new SettingsViewModel(
            coordinator,
            audio,
            home,
            status,
            applyService,
            Fake<IDirectMlAvailability>.Create(),
            npu,
            Fake<IAutoStartService>.Create(),
            new HotkeyCaptureController(hotkey, coordinator),
            models,
            registry,
            loc);

        var window = new Window
        {
            Width = 1000,
            Height = 800,
            Content = new SettingsView { DataContext = vm },
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, window);
    }
}
