namespace TheCurator.ConsoleApp;

class Service : ServiceBase
{
    public Service()
    {
        EventLog.Source = "The Curator";
        container = new ContainerBuilder()
            .UseSQLite()
            .UseReflectedFeatureCatalog()
            .UseBot()
            .Build();
    }

    readonly IContainer container;

    Task ClientLogAsync(LogMessage message)
    {
        if (message.Severity is not LogSeverity.Debug and not LogSeverity.Verbose)
            EventLog.WriteEntry($"{message.Message}{(message.Exception is { } exception ? $"\r\n\r\n{message.Exception.GetFullDetails()}" : string.Empty)}", message.Severity switch
            {
                LogSeverity.Critical or LogSeverity.Error => EventLogEntryType.Error,
                LogSeverity.Warning => EventLogEntryType.Warning,
                _ => EventLogEntryType.Information
            });
        return Task.CompletedTask;
    }

    protected override void OnStart(string[] args)
    {
        var bot = container.Resolve<IBot>();
        var discordToken = string.Empty;
        var discordTokenFilePath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location), "discordToken.txt");
        if (args.Length >= 1 && args[0] is { } commandLineDiscordToken && !string.IsNullOrWhiteSpace(commandLineDiscordToken))
            discordToken = commandLineDiscordToken;
        else if (File.Exists(discordTokenFilePath) && File.ReadAllText(discordTokenFilePath) is { } fileDiscordToken)
            discordToken = fileDiscordToken;
        if (string.IsNullOrWhiteSpace(discordToken))
            throw new Exception("The Discord Token could not be found.");
        bot.Client.Log += ClientLogAsync;
        bot.InitializeAsync(discordToken).Wait();
    }

    protected override void OnStop()
    {
        var bot = container.Resolve<IBot>();
        container.DisposeAsync().AsTask().Wait();
        bot.Client.Log -= ClientLogAsync;
    }
}
