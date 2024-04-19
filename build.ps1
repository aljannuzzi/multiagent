param(
    [switch]$NoBuild = $false,
    [switch]$NoDocker = $false,
    [switch]$NoCompose = $false
)

function GetSecretObject
{
    param([Parameter(Mandatory = $true)][string]$secretGuid)

    $path = "$($env:APPDATA)\Microsoft\UserSecrets\$secretGuid\secrets.json"
    
    ConvertFrom-Json -InputObject $(Get-Content -Path $path -Raw)
}

if (-not $NoBuild)
{
    Write-Output "Building projects..."

    Start-Job -Name "Build SignalRHub" { dotnet publish SignalRHub/SignalRHub.csproj -o build/signalrhub }
    Start-Job -Name "Build TBAStatReader" { dotnet publish TBAStatReader/TBAStatReader.csproj -o build/client }
    Start-Job -Name "Build Orchestrator" { dotnet publish Agents/SignalR/Orchestrator_SignalR/Orchestrator_SignalR.csproj -o build/orchestrator }
    Start-Job -Name "Build Districts_SignalR" { dotnet publish Agents/SignalR/Districts_SignalR/Districts_SignalR.csproj -o build/districtsagent }
    Start-Job -Name "Build Events_SignalR" { dotnet publish Agents/SignalR/Events_SignalR/Events_SignalR.csproj -o build/eventsagent }
    Start-Job -Name "Build Matches_SignalR" { dotnet publish Agents/SignalR/Matches_SignalR/Matches_SignalR.csproj -o build/matchesagent }
    Start-Job -Name "Build Teams_SignalR" { dotnet publish Agents/SignalR/Teams_SignalR/Teams_SignalR.csproj -o build/teamsagent }
    
    Get-Job | Wait-Job | Remove-Job

    Write-Output ""
}

if (-not $NoDocker)
{
    Write-Output "Building Docker images..."

    Start-Job -Name "Build SignalRHub" {
        param($secret)

        docker build -t signalrhub build/signalrhub  --build-arg SIGNALR_CONNSTRING=$($secret.'Azure:SignalR:ConnectionString')
    } -ArgumentList (GetSecretObject 'ff55d15e-c100-4281-8cb5-5d29b4f995ab')

    Start-Job -Name "Build Orchestrator" {
        param($secret)

        $secret = 
        docker build -t orchestrator build/orchestrator --build-arg AZURE_OPENAI_KEY=$secret.AzureOpenAIKey --build-arg SignalREndpoint=http://hub:8080/api/negotiate
    } -ArgumentList (GetSecretObject '1507d29c-61b1-4678-b23a-1562ed1a1abb')

    Start-Job -Name "Build Districts Agent" {
        param($secret)

        $secret = 
        docker build -t districtsagent build/districtsagent --build-arg AZURE_OPENAI_KEY=$secret.AzureOpenAIKey --build-arg SignalREndpoint=http://hub:8080/api/negotiate
    } -ArgumentList (GetSecretObject 'f724ee6c-8bf6-4796-904d-69463aba9287')

    Start-Job -Name "Build Events Agent" {
        param($secret)

        $secret = 
        docker build -t eventsagent build/eventsagent --build-arg AZURE_OPENAI_KEY=$secret.AzureOpenAIKey --build-arg SignalREndpoint=http://hub:8080/api/negotiate
    } -ArgumentList (GetSecretObject 'a74edbc7-6f1b-4f8f-ac34-6a5b90c653cd')

    Start-Job -Name "Build Matches Agent" {
        param($secret)

        $secret = 
        docker build -t matchesagent build/matchesagent --build-arg AZURE_OPENAI_KEY=$secret.AzureOpenAIKey --build-arg SignalREndpoint=http://hub:8080/api/negotiate
    } -ArgumentList (GetSecretObject 'f3b45348-9c0d-4b66-aa62-2228d0369fbe')

    Start-Job -Name "Build Teams Agent" {
        param($secret)

        $secret = 
        docker build -t teamsagent build/teamsagent --build-arg AZURE_OPENAI_KEY=$secret.AzureOpenAIKey --build-arg SignalREndpoint=http://hub:8080/api/negotiate
    } -ArgumentList (GetSecretObject '5631e549-948c-4903-be18-a06152c3600c')
    
    Get-Job | Wait-Job 

    Write-Output ""

    # docker build -t tbaclient build/client
}

if (-not $NoCompose)
{
    Write-Output "Starting Composing..."
    docker compose up
}