using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

public interface IPolicyEngine
{
    PolicyValidationReport Validate(LibrarySnapshot snapshot, LibraryPolicy policy, string policyFingerprint = "");
}
