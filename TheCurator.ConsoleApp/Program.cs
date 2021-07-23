using Autofac;
using System;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using TheCurator.Logic;

namespace TheCurator.ConsoleApp
{
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
                await bot.InitializeAsync(discordToken);
                await Task.Delay(Timeout.InfiniteTimeSpan);
            }
            else
                ServiceBase.Run(new Service());
        }
    }
}
