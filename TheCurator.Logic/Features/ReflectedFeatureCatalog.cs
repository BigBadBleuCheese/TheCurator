namespace TheCurator.Logic.Features;

public class ReflectedFeatureCatalog :
    IFeatureCatalog
{
    public ReflectedFeatureCatalog()
    {
        var featureInterface = typeof(IFeature);
        Services = AppDomain
            .CurrentDomain
            .GetAssemblies()
            .SelectMany(a => a.GetTypes().Where(t => !t.IsInterface && !t.IsAbstract && featureInterface.IsAssignableFrom(t)))
            .ToImmutableArray();
    }

    public IEnumerable<Type> Services { get; }
}
