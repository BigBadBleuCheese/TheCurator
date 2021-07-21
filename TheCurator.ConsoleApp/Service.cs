using Autofac;
using System;
using System.IO;
using System.ServiceProcess;
using TheCurator.Logic;

namespace TheCurator.ConsoleApp
{
    class Service : ServiceBase
    {
        public Service()
        {
            EventLog.Source = "The Curator";
            container = new ContainerBuilder()
                .UseSQLite()
                .UseBot()
                .Build();
        }

        readonly IContainer container;

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
            bot.InitializeAsync(discordToken).Wait();
        }

        protected override void OnStop() => container.DisposeAsync().AsTask().Wait();
    }
}
