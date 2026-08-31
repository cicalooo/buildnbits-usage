using BuildnBits.Usage.Core.Models;

namespace BuildnBits.Usage.Core.Providers;

public interface IUsageProvider
{
    Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken);
}
