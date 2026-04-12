using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Profiles;

internal interface ISynchronousRunProfileStore
{
    IReadOnlyList<RunProfileDocument> ListSynchronously();
}