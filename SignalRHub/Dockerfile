FROM mcr.microsoft.com/dotnet/aspnet:8.0
USER app

ARG SIGNALR_CONNSTRING

ENV Azure__SignalR__ConnectionString=$SIGNALR_CONNSTRING
ENV ASPNETCORE_HTTP_PORTS=8080

WORKDIR /app

COPY . .

EXPOSE 8080

ENTRYPOINT ["dotnet", "SignalRHub.dll"]