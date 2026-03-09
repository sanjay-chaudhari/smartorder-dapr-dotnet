using System.Runtime.Serialization;

namespace InventoryService.Models;

// All types used in Dapr actor remoting must be decorated with [DataContract]/[DataMember]
// because the actor runtime uses DataContractSerializer, not System.Text.Json.

[DataContract]
public record ReserveInventoryRequest(
    [property: DataMember] string ProductId,
    [property: DataMember] int Quantity,
    [property: DataMember] string OrderId);

[DataContract]
public record ReserveInventoryResponse(
    [property: DataMember] bool Success,
    [property: DataMember] string? FailureReason);

[DataContract]
public record ReleaseInventoryRequest(
    [property: DataMember] string ProductId,
    [property: DataMember] int Quantity,
    [property: DataMember] string OrderId);

[DataContract]
public record ReleaseInventoryResponse(
    [property: DataMember] bool Success);

// State store record — key: inventory-{productId}
[DataContract]
public record InventoryItem(
    [property: DataMember] string ProductId,
    [property: DataMember] int AvailableQuantity,
    [property: DataMember] int ReservedQuantity);
