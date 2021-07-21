using System;
using System.Threading.Tasks;

namespace TheCurator.Logic
{
    public interface IBot : IDisposable
    {
        Task InitializeAsync(string token);
    }
}
