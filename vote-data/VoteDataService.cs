namespace VoteData;

public class VoteDataService
{
    private readonly ActivitySource _activitySource;

    private readonly IDatabase _redis;

    //private readonly Counter<long> _resetCounter;
    private readonly VoteSettings _settings;

    public VoteDataService(IConnectionMultiplexer multiplexer, IOptions<VoteSettings> settings,
        ActivitySource activitySource
        //, Meter meter
    )
    {
        _redis = multiplexer.GetDatabase();
        _activitySource = activitySource;
        _settings = settings.Value;
        //_resetCounter = meter.CreateCounter<long>("reset_count", description: "Counts number of resets");
    }

    public async Task<Result> GetVotesAsync()
    {
        using var activity = _activitySource.StartActivity();
        var vote1Count = await _redis.StringGetAsync(CacheKeys.Vote1Key);
        var vote2Count = await _redis.StringGetAsync(CacheKeys.Vote2Key);
        return new(
            new(_settings.Vote1Label, vote1Count.TryParse(out long val1) ? val1 : 0),
            new(_settings.Vote2Label, vote2Count.TryParse(out long val2) ? val2 : 0));
    }

    public async Task ResetVotesAsync()
    {
        using var activity = _activitySource.StartActivity();
        activity?.AddEvent(new("Reset event"));
        await _redis.StringSetAsync(CacheKeys.Vote1Key, 0);
        await _redis.StringSetAsync(CacheKeys.Vote2Key, 0);
        //_resetCounter.Add(1);
    }
}