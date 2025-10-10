using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace CabaVS.Common.Infrastructure.Storage;

public interface IBlobConnectionProvider
{
    BlobServiceClient GetBlobServiceClient();
    BlobClient GetBlobClient(Uri blobUri);
}

public sealed class BlobConnectionProvider(
    IConfiguration configuration,
    bool useIdentity = true,
    string connectionStringName = "BlobStorage") : IBlobConnectionProvider
{
    public BlobServiceClient GetBlobServiceClient()
    {
        var connectionString = GetConnectionString();
        
        return useIdentity
            ? new BlobServiceClient(new Uri(connectionString), new DefaultAzureCredential())
            : new BlobServiceClient(connectionString);
    }

    public BlobClient GetBlobClient(Uri blobUri)
    {
        if (useIdentity)
        {
            return new BlobClient(blobUri, new DefaultAzureCredential());
        }
        
        var connectionString = GetConnectionString();
        
#pragma warning disable IDE0008
        var (accountName, accountKey) = ParseAccountCredentials(connectionString);
#pragma warning restore IDE0008
        
        return new BlobClient(blobUri, new StorageSharedKeyCredential(accountName, accountKey));
    }
    
    private string GetConnectionString() =>
        configuration.GetConnectionString(connectionStringName) ??
        configuration[connectionStringName] ??
        throw new InvalidOperationException($"Connection string '{connectionStringName}' is not configured.");
    
    private static (string AccountName, string AccountKey) ParseAccountCredentials(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);

        string? accountName = null;
        string? accountKey = null;

        foreach (var part in parts)
        {
            if (part.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase))
            {
                accountName = part["AccountName=".Length..];
            }
            else if (part.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase))
            {
                accountKey = part["AccountKey=".Length..];
            }
        }

        return string.IsNullOrEmpty(accountName) || string.IsNullOrEmpty(accountKey)
            ? throw new InvalidOperationException("Invalid connection string: missing AccountName or AccountKey.")
            : (accountName, accountKey);
    }
}
