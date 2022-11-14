using Blazored.Toast;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using VoteUI;
using VoteUI.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddBlazoredToast();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<VoteService>();
builder.Services.AddHttpClient<VoteDataClient>(client =>
    client.BaseAddress = new($"http://{builder.Configuration["Hosts:VoteDataService"]!}:8081"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();