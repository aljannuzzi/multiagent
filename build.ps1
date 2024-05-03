param(
    [switch]$NoBuild,
    [switch]$NoDocker,
    $Deploy = [DeploymentType]::None
)

function GetSecretObject
{
    param([Parameter(Mandatory = $true)][string]$secretGuid)

    $path = "$($env:APPDATA)\Microsoft\UserSecrets\$secretGuid\secrets.json"
    
    ConvertFrom-Json -InputObject $(Get-Content -Path $path -Raw)
}

Write-Output $PSScriptRoot

if (-not $NoBuild)
{
    Write-Output "Building projects..."

    Start-Job -Name "Build SignalRHub" { param($root) dotnet publish $root\SignalRHub\SignalRHub.csproj -o $root\build\signalrhub } -ArgumentList $PSScriptRoot
    Start-Job -Name "Build TBAStatReader" { param($root) dotnet publish $root\TBAStatReader\TBAStatReader.csproj -o $root\build\client } -ArgumentList $PSScriptRoot
    Start-Job -Name "Build Orchestrator" { param($root) dotnet publish $root\Agents\SignalR\Orchestrator_SignalR\Orchestrator_SignalR.csproj -o $root\build\orchestrator } -ArgumentList $PSScriptRoot
    Start-Job -Name "Build Districts_SignalR" { param($root) dotnet publish $root\Agents\SignalR\Districts_SignalR\Districts_SignalR.csproj -o $root\build\districtsagent } -ArgumentList $PSScriptRoot
    Start-Job -Name "Build Events_SignalR" { param($root) dotnet publish $root\Agents\SignalR\Events_SignalR\Events_SignalR.csproj -o $root\build\eventsagent } -ArgumentList $PSScriptRoot
    Start-Job -Name "Build Matches_SignalR" { param($root) dotnet publish $root\Agents\SignalR\Matches_SignalR\Matches_SignalR.csproj -o $root\build\matchesagent } -ArgumentList $PSScriptRoot
    Start-Job -Name "Build Teams_SignalR" { param($root) dotnet publish $root\Agents\SignalR\Teams_SignalR\Teams_SignalR.csproj -o $root\build\teamsagent } -ArgumentList $PSScriptRoot
    
    Get-Job | Wait-Job #| Remove-Job

    Write-Output ""
}

if (-not $NoDocker)
{
    docker rmi -f signalrhub orchestrator districtsagent eventsagent matchesagent teamsagent > $null

    Write-Output "Building Docker images..."

    Start-Job -Name "Build SignalRHub" {
        param($secret, $root)

        docker build -t signalrhub $root\build/signalrhub --build-arg SIGNALR_CONNSTRING=$($secret.'Azure:SignalR:ConnectionString')
    } -ArgumentList (GetSecretObject 'ff55d15e-c100-4281-8cb5-5d29b4f995ab'), $PSScriptRoot

    Start-Job -Name "Build Orchestrator" {
        param($secret, $root)

        docker build -t orchestrator $root/build/orchestrator --build-arg AZURE_OPENAI_KEY=$($secret.AzureOpenAIKey) --build-arg SignalREndpoint=http://hub:8080/api/negotiate
    } -ArgumentList (GetSecretObject '1507d29c-61b1-4678-b23a-1562ed1a1abb'), $PSScriptRoot

    Start-Job -Name "Build Districts Agent" {
        param($secret, $root)

        docker build -t districtsagent $root/build/districtsagent --build-arg AZURE_OPENAI_KEY=$($secret.AzureOpenAIKey) --build-arg SignalREndpoint=http://hub:8080/api/negotiate --build-arg TBA_API_KEY=$($secret.TBA_API_KEY)
    } -ArgumentList (GetSecretObject 'f724ee6c-8bf6-4796-904d-69463aba9287'), $PSScriptRoot

    Start-Job -Name "Build Events Agent" {
        param($secret, $root)

        docker build -t eventsagent $root/build/eventsagent --build-arg AZURE_OPENAI_KEY=$($secret.AzureOpenAIKey) --build-arg SignalREndpoint=http://hub:8080/api/negotiate --build-arg TBA_API_KEY=$($secret.TBA_API_KEY)
    } -ArgumentList (GetSecretObject 'a74edbc7-6f1b-4f8f-ac34-6a5b90c653cd'), $PSScriptRoot

    Start-Job -Name "Build Matches Agent" {
        param($secret, $root)

        docker build -t matchesagent $root/build/matchesagent --build-arg AZURE_OPENAI_KEY=$($secret.AzureOpenAIKey) --build-arg SignalREndpoint=http://hub:8080/api/negotiate --build-arg TBA_API_KEY=$($secret.TBA_API_KEY)
    } -ArgumentList (GetSecretObject 'f3b45348-9c0d-4b66-aa62-2228d0369fbe'), $PSScriptRoot

    Start-Job -Name "Build Teams Agent" {
        param($secret, $root)

        docker build -t teamsagent $root/build/teamsagent --build-arg AZURE_OPENAI_KEY=$($secret.AzureOpenAIKey) --build-arg SignalREndpoint=http://hub:8080/api/negotiate --build-arg TBA_API_KEY=$($secret.TBA_API_KEY)
    } -ArgumentList (GetSecretObject '5631e549-948c-4903-be18-a06152c3600c'), $PSScriptRoot
    
    Get-Job | Wait-Job 

    Write-Output ""
}

if ($Deploy -eq [DeploymentType]::Kubernetes)
{
    Write-Output "Deploying to Kubernetes..."
    kubectl apply -f $PSScriptRoot\k8s.deploy.yml
}
elseif ($Deploy -eq [DeploymentType]::Docker) {
    Write-Output "Deploying via Docker Compose..."
    docker compose up -d --no-build -f $PSScriptRoot\compose.yml
}

enum DeploymentType
{
    Kubernetes
    Docker
    None
}
