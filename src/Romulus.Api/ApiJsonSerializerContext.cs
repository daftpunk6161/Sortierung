using System.Text.Json.Serialization;

namespace Romulus.Api;

[JsonSerializable(typeof(ApiReviewApprovalRequest))]
internal sealed partial class ApiJsonSerializerContext : JsonSerializerContext
{
}
