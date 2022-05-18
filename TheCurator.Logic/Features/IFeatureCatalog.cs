namespace TheCurator.Logic.Features;

public interface IFeatureCatalog
{
    IEnumerable<Type> Services { get; }
}
