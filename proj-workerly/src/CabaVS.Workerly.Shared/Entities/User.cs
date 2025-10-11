namespace CabaVS.Workerly.Shared.Entities;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
}
