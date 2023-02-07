using System;
using System.Diagnostics;
using System.Threading;
using Common;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using StackExchange.Redis;

IConfiguration config = new ConfigurationBuilder().AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();

// Redis connection
var redisConnection = ConnectionMultiplexer.Connect(config["Hosts:Redis"]);
var redis = redisConnection.GetDatabase();

var factory = new ConnectionFactory { HostName = config["Queue:Host"], AutomaticRecoveryEnabled = true };
var connection = factory.CreateConnection();
using var channel = connection.CreateModel();
channel.QueueDeclare(config["Queue:Name"], autoDelete: false, exclusive: false);
var consumer = new EventingBasicConsumer(channel);

consumer.Received += async (_, eventArgs) =>
{
    // Process the message
    var body = eventArgs.Body.ToArray();
    var candidate = BitConverter.ToInt32(body);
    var currentValue = candidate switch
    {
        1 => await redis.StringIncrementAsync(CacheKeys.Vote1Key),
        2 => await redis.StringIncrementAsync(CacheKeys.Vote2Key),
        _ => throw new UnreachableException(),
    };
};

channel.BasicConsume(config["Queue:Name"], true, consumer);

// Prevent main thread from exiting.
var mre = new ManualResetEvent(false);
mre.WaitOne();