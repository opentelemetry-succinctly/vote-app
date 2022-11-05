IConfiguration config =
    new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .AddEnvironmentVariables()
        .Build();

// Hack: Giving time to RabbitMQ to start. Use a retry policy in production.
Thread.Sleep(TimeSpan.FromSeconds(30));

var redisConnection = ConnectionMultiplexer.Connect(config.GetConnectionString("RedisHost"));
var redis = redisConnection.GetDatabase();

var factory = new ConnectionFactory { HostName = config["Queue:HostName"], AutomaticRecoveryEnabled = true };
var connection = factory.CreateConnection();
using var channel = connection.CreateModel();
channel.QueueDeclare(config["Queue:Name"], autoDelete: false, exclusive: false);

var consumer = new EventingBasicConsumer(channel);
consumer.Received += async (_, eventArgs) =>
{
    //using var activity = _activitySource.StartActivity(nameof(IncrementVoteAsync), ActivityKind.Server);
    //activity?.AddEvent(new("Vote added"));
    //activity?.SetTag(nameof(candidate), candidate);

    var body = eventArgs.Body.ToArray();
    var candidate = BitConverter.ToInt32(body);
    var currentValue = candidate switch
    {
        1 => await redis.StringIncrementAsync(CacheKeys.Vote1Key),
        2 => await redis.StringIncrementAsync(CacheKeys.Vote2Key),
        _ => throw new ArgumentOutOfRangeException(nameof(candidate))
    };

    // save currentvalue in meter
    //_votesCounter.Add(1, tag: new("candidate", Vote1Key));
};

channel.BasicConsume(config["Queue:Name"], true, consumer);

// Prevent main thread from exiting.
var mre = new ManualResetEvent(false);
mre.WaitOne();