using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using VoteData;

var builder = WebApplication.CreateBuilder(args);

// Add Redis service
var redisConnection = ConnectionMultiplexer.Connect(builder.Configuration["Hosts:Redis"]);
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);

builder.Services.AddSingleton<VoteDataService>();

// Application settings
builder.Services.Configure<VoteSettings>(builder.Configuration.GetSection(nameof(VoteSettings)));

var app = builder.Build();

// API Endpoints
var apiGroup = app.MapGroup("/vote").WithDescription("Vote Data API");
apiGroup.MapGet("/", static async (VoteDataService vds) => await vds.GetVotesAsync());
apiGroup.MapPost("/reset", static async (VoteDataService vds) => await vds.ResetVotesAsync());

app.Run();