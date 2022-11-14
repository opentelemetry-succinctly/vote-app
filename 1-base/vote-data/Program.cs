using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VoteData;

var builder = WebApplication.CreateBuilder(args);

// Add Redis service
var redisConnection = ConnectionMultiplexer.Connect($"{builder.Configuration["Hosts:Redis"]}");
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

builder.Services.AddSingleton<VoteDataService>();

// Application settings
builder.Services.Configure<VoteSettings>(builder.Configuration.GetSection(nameof(VoteSettings)));

var app = builder.Build();

// API Endpoints
app.MapGet("/vote", static async (VoteDataService vds) => await vds.GetVotesAsync());
app.MapPost("/vote/reset", static async (VoteDataService vds) => await vds.ResetVotesAsync());

app.Run();