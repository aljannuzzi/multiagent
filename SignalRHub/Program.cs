using System.Text.Json;
using System.Text.Json.Serialization;

using Common;

using Microsoft.AspNetCore.SignalR;

using SignalRASPHub;

internal class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddSignalR(o =>
        {
            o.MaximumParallelInvocationsPerClient = 2;
        })
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions = new JsonSerializerOptions
                {
                    Converters = { new SynchronousIAsyncEnumerableConverter() },
                };
            })
            .AddAzureSignalR();

        builder.Services.AddSingleton<IUserIdProvider, UserIdProvider>();
        WebApplication app = builder.Build();

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

    private class SynchronousIAsyncEnumerableConverter : JsonConverter<IAsyncEnumerable<string>>
    {
        public override IAsyncEnumerable<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotImplementedException();
        public override void Write(Utf8JsonWriter writer, IAsyncEnumerable<string> value, JsonSerializerOptions options) => throw new NotImplementedException();
    }
}