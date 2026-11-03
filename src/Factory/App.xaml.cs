using Factory.Services;
using Factory.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Factory;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? Window { get; private set; }
    /// <summary>Pipeline dev → homolog → prod: `--homolog` roda ao lado da prod, com
    /// titulo marcado e a agente de TESTE (agent-test.json). Prod nunca recebe mudanca sem passar por aqui.</summary>
    public static bool IsHomolog { get; private set; }

    public App()
    {
        InitializeComponent();
        // The homolog build once closed by itself with nothing in the Event Log. Now every unhandled exception nao tratada vai pra
        // %LOCALAPPDATA%\Factory\odin-log\crash.txt e, quando for de UI, o app segue vivo (Handled).
        UnhandledException += (_, e) =>
        {
            LogCrash("UI", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { LogCrash("Task", e.Exception); e.SetObserved(); };
    }

    public static void LogCrash(string origem, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(AppPaths.Cache, "odin-log");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.txt"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {origem}: {ex}" + Environment.NewLine + Environment.NewLine);
        }
        catch (Exception) { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        IsHomolog = (args.Arguments ?? "").Contains("--homolog", StringComparison.OrdinalIgnoreCase)
                    || Environment.GetCommandLineArgs().Any(a => a.Equals("--homolog", StringComparison.OrdinalIgnoreCase));
        // Instancia unica por ambiente: a segunda invocacao so traz a primeira pra frente.
        var instance = AppInstance.FindOrRegisterForKey(IsHomolog ? "factory-homolog" : "factory-main");
        if (!instance.IsCurrent)
        {
            instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs())
                .AsTask().GetAwaiter().GetResult();
            Environment.Exit(0);
            return;
        }
        instance.Activated += (_, e) =>
        {
            // Segunda invocacao (atalho global Ctrl+Alt+O = "Factory.exe --odin"): traz pra frente e chama o Odin.
            var odin = (e.Data as Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs)?.Arguments?.Contains("--odin", StringComparison.OrdinalIgnoreCase) == true;
            Window?.DispatcherQueue.TryEnqueue(() => { Window.Activate(); NoMonitorDoOdin(); if (odin) Window.SummonOdin(); });
        };

        Services = ConfigureServices();
        // Porta local das maos (127.0.0.1:8788) pra orb online mandar as ferramentas pra ca.
        // OPT-IN: so com `--maos-online` (security decision: without the flag no port opens;
        // com ela, o web-orb\server.mjs + tunel expoem as maos e quem tiver o link manda no PC).
        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--maos-online", StringComparison.OrdinalIgnoreCase)))
            new HandsServer().Start();
        Window = new MainWindow();
        Window.Activate();
        // VIGIA da Seguranca: monitoramento continuo em segundo plano (so le; 1 instancia por vez, ver SecurityWatch)
        try { Services.GetRequiredService<ISecurityWatch>().Iniciar(); } catch (Exception ex) { LogCrash("SecurityWatch.Iniciar", ex); }
        Window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, NoMonitorDoOdin);
        // an unpackaged app receives EMPTY args.Arguments; o Ctrl+Alt+O com o Factory fechado abria sem ligar a voz.
        if ((args.Arguments ?? "").Contains("--odin", StringComparison.OrdinalIgnoreCase)
            || Environment.GetCommandLineArgs().Any(a => a.Equals("--odin", StringComparison.OrdinalIgnoreCase)))
            Window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => Window.SummonOdin());
    }

    /// <summary>Optional placement: with FACTORY_MONITOR=N the Odin/Factory window opens maximized on monitor N (on start
    /// and on Ctrl+Alt+O). Unset, or a monitor that does not exist: the window stays where Windows put it.</summary>
    private static void NoMonitorDoOdin()
    {
        try
        {
            if (Window is null || !int.TryParse(Environment.GetEnvironmentVariable("FACTORY_MONITOR"), out var n)
                || n < 1 || n > WindowsHands.MonitorCount) return;
            var h = WinRT.Interop.WindowNative.GetWindowHandle(Window);
            WindowsHands.Arrange(h, "maximizar", n.ToString());
        }
        catch (Exception) { /* posicao e conforto; nunca derruba o app */ }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISessionIndex, SessionIndex>();
        services.AddSingleton<IStatusParser, StatusParser>();
        services.AddSingleton<IManifestStore, ManifestStore>();
        services.AddSingleton<IGitProbe, GitProbe>();
        services.AddSingleton<IProjectScanner, ProjectScanner>();
        services.AddSingleton<ProjectCatalog>();
        services.AddSingleton<ILauncher, Launcher>();
        services.AddSingleton<IContextBundler, ContextBundler>();
        services.AddSingleton<IProjectForge, ProjectForge>();
        services.AddSingleton<ISystemProbe, SystemProbe>();
        services.AddSingleton<ITrailService, TrailService>();
        services.AddSingleton<IBriefingService, BriefingService>();
        services.AddSingleton<IAudioStudio, AudioStudio>();
        services.AddSingleton<IAppLibrary, AppLibrary>();
        services.AddSingleton<IVidaLibrary, LifeLibrary>();
        services.AddSingleton<AudioPlayer>();
        services.AddSingleton<IRadarService, RadarService>();
        services.AddSingleton<IHostSecurityService, HostSecurityService>();
        services.AddSingleton<LiveTelemetry>();
        services.AddSingleton<ISecuritySquad, SecuritySquad>();
        services.AddSingleton<ISecurityWatch, SecurityWatch>();
        services.AddSingleton<NavigationService>();
        services.AddSingleton<ICommandLine, CommandLine>();
        services.AddSingleton<IChatService, ChatService>();
        services.AddSingleton<IVoiceAgent, VoiceAgent>();
        services.AddSingleton<IOdinHands, OdinHands>();
        services.AddSingleton<IInboxService, InboxService>();
        services.AddSingleton<ISetupInventory, SetupInventory>();
        services.AddSingleton<ISetupUsage, SetupUsage>();
        services.AddSingleton<IPlanoService, BoardService>();

        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<AppsViewModel>();
        services.AddSingleton<LifeViewModel>();
        services.AddSingleton<RadarViewModel>();
        services.AddSingleton<SecurityViewModel>();
        services.AddSingleton<ProjectsViewModel>();
        services.AddSingleton<FleetViewModel>();
        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<BoardViewModel>();
        services.AddSingleton<InboxViewModel>();
        services.AddSingleton<PaletteViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ContextsViewModel>();
        services.AddSingleton<MoneyViewModel>();
        services.AddSingleton<ForgeViewModel>();
        services.AddSingleton<StudioViewModel>();

        return services.BuildServiceProvider();
    }
}
