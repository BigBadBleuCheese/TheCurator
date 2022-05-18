using Autofac;
using Cogs.Exceptions;
using Discord;
using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
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
                .UseReflectedFeatureCatalog()
                .UseBot()
                .Build();
        }

        readonly IContainer container;

        Task ClientLogAsync(LogMessage message)
        {
            EventLog.WriteEntry($"An unexpected event occurred when communicating with the Discord API: {message.Message} ({message.Severity})\r\n{message.Exception.GetFullDetails()}", EventLogEntryType.Warning);
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
}
