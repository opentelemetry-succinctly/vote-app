using System.Threading.Tasks;
using Common;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace VoteData;

public class VoteDataService
{
    private readonly IDatabase _redis;
    private readonly VoteSettings _settings;

    public VoteDataService(IConnectionMultiplexer multiplexer, IOptions<VoteSettings> settings)
    {
        _redis = multiplexer.GetDatabase();
        _settings = settings.Value;
    }

    public async Task<Result> GetVotesAsync()
    {
        var vote1Count = await _redis.StringGetAsync(CacheKeys.Vote1Key);
        var vote2Count = await _redis.StringGetAsync(CacheKeys.Vote2Key);
        return new(new(_settings.Vote1Label, vote1Count.TryParse(out long val1) ? val1 : 0),
            new(_settings.Vote2Label, vote2Count.TryParse(out long val2) ? val2 : 0));
    }

    public async Task ResetVotesAsync()
    {
        await _redis.StringSetAsync(CacheKeys.Vote1Key, 0);
        await _redis.StringSetAsync(CacheKeys.Vote2Key, 0);
    }
}