using Autofac;
using TheCurator.Logic.Data;
using TheCurator.Logic.Data.SQLite;

namespace TheCurator.Logic
{
    public static class ConfigurationExtensions
    {
        public static ContainerBuilder UseBot(this ContainerBuilder builder)
        {
            builder.RegisterType<Bot>().As<IBot>();
            return builder;
        }

        public static ContainerBuilder UseSQLite(this ContainerBuilder builder)
        {
            builder.RegisterType<DataStore>().As<IDataStore>();
            return builder;
        }
    }
}
