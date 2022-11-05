namespace VoteUI.Data;

public class VoteService
{
    private readonly ActivitySource _activitySource;
    private readonly IConfiguration _config;
    private readonly VoteDataClient _voteDataClient;

    public VoteService(VoteDataClient voteDataClient, ActivitySource activitySource, IConfiguration config)
    {
        _voteDataClient = voteDataClient;
        _activitySource = activitySource;
        _config = config;
    }

    public async Task<(Vote vote1, Vote vote2)> GetVotesAsync()
    {
        using var activity = _activitySource.StartActivity();

        var response = await _voteDataClient.Client.GetFromJsonAsync<Result>("/vote");
        return (response!.Vote1, response.Vote2);
    }

    public void IncrementVote(int candidate)
    {
        using var activity = _activitySource.StartActivity();
        activity?.AddEvent(new("Vote added"));
        activity?.SetTag(nameof(candidate), candidate);

        // Refer to RabbitMQ guide for best practices https://www.rabbitmq.com/dotnet-api-guide.html
        var factory = new ConnectionFactory { HostName = _config["Queue:HostName"] };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(_config["Queue:Name"], autoDelete: false, exclusive: false);
        channel.BasicPublish(string.Empty, "votes", body: BitConverter.GetBytes(candidate));
    }

    public async Task ResetVotesAsync()
    {
        using var activity = _activitySource.StartActivity();
        activity?.AddEvent(new("Reset event"));
        await _voteDataClient.Client.PostAsync("/vote/reset", null);
    }
}