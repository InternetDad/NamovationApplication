$ErrorActionPreference = 'Stop'
$password = Read-Host 'PostgreSQL password for user postgres' -AsSecureString
$credential = [System.Management.Automation.PSCredential]::new('postgres', $password)
$plainPassword = $credential.GetNetworkCredential().Password
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=Namovation;Username=postgres;Password=$plainPassword"
try {
    dotnet run --project (Join-Path $PSScriptRoot '..\Namovation.Web\Namovation.Web.csproj')
}
finally {
    Remove-Item Env:ConnectionStrings__DefaultConnection -ErrorAction SilentlyContinue
    $plainPassword = $null
    $password.Dispose()
}
