namespace TheCurator.ConsoleApp;

class Program
{
    static async Task Main(string[] args)
    {
        if (Environment.UserInteractive)
        {
            var container = new ContainerBuilder()
                .UseSQLite()
                .UseReflectedFeatureCatalog()
                .UseBot()
                .Build();
            var bot = container.Resolve<IBot>();
            if (args.Length < 1 || args[0] is not { } discordToken || string.IsNullOrWhiteSpace(discordToken))
                throw new Exception("The Discord Token could not be found.");
            bot.Client.Log += ClientLogAsync;
            await bot.InitializeAsync(discordToken);
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        else
            ServiceBase.Run(new Service());
    }

    static Task ClientLogAsync(LogMessage message)
    {
        Console.WriteLine($"[{message.Severity}] {message.Message}");
        if (message.Exception is { } ex)
            Console.WriteLine(message.Exception?.GetFullDetails());
        return Task.CompletedTask;
    }
}
