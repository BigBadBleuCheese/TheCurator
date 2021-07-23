using Autofac;
using TheCurator.Logic.Data;
using TheCurator.Logic.Data.SQLite;
using TheCurator.Logic.Features;

namespace TheCurator.Logic
{
    public static class ConfigurationExtensions
    {
        public static ContainerBuilder UseBot(this ContainerBuilder builder)
        {
            builder.RegisterType<Bot>().As<IBot>();
            return builder;
        }

        public static ContainerBuilder UseReflectedFeatureCatalog(this ContainerBuilder builder)
        {
            builder.RegisterInstance(new ReflectedFeatureCatalog()).As<IFeatureCatalog>();
            return builder;
        }

        public static ContainerBuilder UseSQLite(this ContainerBuilder builder)
        {
            builder.RegisterType<DataStore>().As<IDataStore>();
            return builder;
        }
    }
}
