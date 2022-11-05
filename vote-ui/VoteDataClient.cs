namespace VoteUI;

public record VoteDataClient
{
    public VoteDataClient(HttpClient httpClient, IConfiguration configuration)
    {
        httpClient.BaseAddress = new(configuration.GetConnectionString("VoteDataServiceUrl"));
        Client = httpClient;
    }

    public HttpClient Client { get; }
}