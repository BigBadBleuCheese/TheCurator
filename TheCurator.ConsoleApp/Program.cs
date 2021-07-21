using Autofac;
using Cogs.Exceptions;
using System;
using System.Threading;
using System.Threading.Tasks;
using TheCurator.Logic;

namespace TheCurator.ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var container = new ContainerBuilder()
                .UseSQLite()
                .UseBot()
                .Build();
            var bot = container.Resolve<IBot>();
            if (args.Length < 1 || args[0] is not { } discordToken || string.IsNullOrWhiteSpace(discordToken))
            {
                Console.Error.WriteLine("FATAL: The Discord Token could not be found.");
                return;
            }
            try
            {
                await bot.InitializeAsync(discordToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FATAL: An unexpected exception was encountered during initialization.{Environment.NewLine}{Environment.NewLine}{ex.GetFullDetails()}");
                return;
            }
            Console.WriteLine("Start successful.");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
    }
}
