using Common;

using Microsoft.AspNetCore.SignalR;

using SignalRASPHub;

internal class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddUserSecrets<TbaSignalRHub>();

        // Add services to the container.
        builder.Services.AddSignalR(o =>
        {
            o.MaximumParallelInvocationsPerClient = 2;
        }).AddAzureSignalR();


        builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>();
        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.MapHub<TbaSignalRHub>("/api");
        app.Run();
    }
}