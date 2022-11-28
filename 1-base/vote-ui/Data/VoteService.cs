using System;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace VoteUI.Data;

public class VoteService
{
    private readonly IConfiguration _config;
    private readonly VoteDataClient _voteDataClient;

    public VoteService(VoteDataClient voteDataClient, IConfiguration config)
    {
        _voteDataClient = voteDataClient;
        _config = config;
    }

    public async Task<(Vote vote1, Vote vote2)> GetVotesAsync()
    {
        var response = await _voteDataClient.Client.GetFromJsonAsync<Result>("/vote");
        return (response!.Vote1, response.Vote2);
    }

    public void IncrementVote(int candidate)
    {
        // Refer to RabbitMQ guide for best practices https://www.rabbitmq.com/dotnet-api-guide.html
        var factory = new ConnectionFactory { HostName = _config["Queue:Host"] };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(_config["Queue:Name"], autoDelete: false, exclusive: false);
        channel.BasicPublish(string.Empty, "votes", body: BitConverter.GetBytes(candidate));
    }

    public async Task ResetVotesAsync()
    {
        await _voteDataClient.Client.PostAsync("/vote/reset", null);
    }
}