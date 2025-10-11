namespace CabaVS.Workerly.Shared.Configuration;

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string Key { get; set; } = "";
    public string Database { get; set; } = "";
    public ContainerNames Containers { get; set; } = new();

    public sealed class ContainerNames
    {
        public string Users { get; set; } = "";
        public string Workspaces { get; set; } = "";
        public string WorkspaceConfigs { get; set; } = "";
        public string Memberships { get; set; } = "";
    }
}
