FROM mcr.microsoft.com/dotnet/aspnet:8.0
USER app

ARG AZURE_OPENAI_KEY
ENV AzureOpenAIKey=$AZURE_OPENAI_KEY

ARG SignalREndpoint
ENV SignalREndpoint=$SignalREndpoint

WORKDIR /app

COPY . .

ENTRYPOINT ["dotnet", "Matches_SignalR.dll"]