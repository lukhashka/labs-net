# Lab 10, Part 1: Kubernetes manifests of the whole platform from this AppHost with Aspir8 (aspirate).
#   .\aspirate-generate.ps1            # manifests only
#   .\aspirate-generate.ps1 -Build     # also builds the container images of the projects (dotnet publish /t:PublishContainer)
# then: dotnet aspirate apply   (or kubectl apply -k .\aspirate-output after aspirate wrote the .secrets files)
#
# Secrets for the cluster are random. aspirate keeps them encrypted in aspirate-state.json with the state password;
# that file and the .secrets files it writes during apply are in .gitignore. The JWT signing key is not part of the
# manifests at all: it comes from the Secret platform-jwt (deploy/k8s/README.md).
param(
    [switch]$Build,
    [string]$StatePassword = $env:ASPIRATE_SECRET_PASSWORD
)

if (-not $StatePassword) { $StatePassword = Read-Host "Password of the aspirate secret state" }

function New-Secret([int]$Length = 24) {
    -join ((48..57) + (65..90) + (97..122) | Get-Random -Count $Length | ForEach-Object { [char]$_ })
}

# aspirate 9.1 targets .NET 9; on a PC with only .NET 10 it runs on the newer runtime
$env:DOTNET_ROLL_FORWARD = "Major"

$arguments = @(
    "aspirate", "generate", "--non-interactive",
    "--launch-profile", "http",
    "--image-pull-policy", "IfNotPresent",          # Docker Desktop: local images are visible to the cluster
    "--container-image-tag", "v1",
    "--output-format", "kustomize",
    "--include-dashboard", "false",
    "--secret-password", $StatePassword,
    "--parameter", "sql-password=Sq1!$(New-Secret)",  # SQL Server needs a digit, a letter case mix and a symbol
    "--parameter", "mongo-password=$(New-Secret)",
    "--parameter", "redis-password=$(New-Secret)",
    "--parameter", "messaging-password=$(New-Secret)",
    "--parameter", "keycloak-password=$(New-Secret)"
)
if (-not $Build) { $arguments += "--skip-build" }

Push-Location $PSScriptRoot
try {
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "aspirate generate failed" }

    # aspirate 9.1 makes Secrets only for SQL Server and connection strings; the passwords of the other containers
    # (MongoDB, Redis, RabbitMQ, Keycloak) it writes as ConfigMap literals. They are moved into a local env file,
    # a secretGenerator and a secretRef of the workload. The values stay the same as in the connection strings.
    $sensitive = '^\s+- ([A-Za-z0-9_]*(PASSWORD|PASS|SECRET|TOKEN)[A-Za-z0-9_]*)=(.*)$'
    foreach ($dir in Get-ChildItem aspirate-output -Directory) {
        $kustomization = Join-Path $dir.FullName "kustomization.yaml"
        $lines = Get-Content $kustomization
        $moved = $lines | Where-Object { $_ -match $sensitive }
        if (-not $moved) { continue }

        $name = $dir.Name
        $envFile = ".$name.protected.secrets"
        $moved | ForEach-Object { ($_ -replace $sensitive, '$1=$3').Trim() } | Set-Content (Join-Path $dir.FullName $envFile) -Encoding ascii
        $kept = $lines | Where-Object { $_ -notmatch $sensitive }
        $kept += @("", "secretGenerator:", "- name: $name-protected", "  envs:", "  - $envFile")
        $kept | Set-Content $kustomization -Encoding utf8

        $workload = Get-ChildItem $dir.FullName -Include statefulset.yaml, deployment.yaml -Recurse | Select-Object -First 1
        $yaml = Get-Content $workload.FullName
        $out = foreach ($line in $yaml) {
            $line
            if ($line -match "^(\s+)name: $name-env\s*$") {
                $indent = ($Matches[1].Length - 4)
                (' ' * $indent) + "- secretRef:"
                (' ' * ($indent + 4)) + "name: $name-protected"
            }
        }
        $out | Set-Content $workload.FullName -Encoding utf8
        Write-Host "$name`: $($moved.Count) value(s) moved to Secret $name-protected"
    }

    # The JWT signing key of Lab 09 comes from the Secret platform-jwt created by kubectl (deploy/k8s/README.md).
    # optional: true - a pod still starts without it in a test cluster, Production then refuses to start (no key).
    foreach ($deployment in Get-ChildItem aspirate-output -Recurse -Filter deployment.yaml) {
        $yaml = Get-Content $deployment.FullName
        $out = for ($i = 0; $i -lt $yaml.Count; $i++) {
            $yaml[$i]
            # the new item gets the indentation of the existing items of the list
            if ($yaml[$i] -match '^\s+envFrom:\s*$' -and $i + 1 -lt $yaml.Count -and $yaml[$i + 1] -match '^(\s+)- ') {
                $indent = $Matches[1]
                "$indent- secretRef:"
                "$indent    name: platform-jwt"
                "$indent    optional: true"
            }
        }
        $out | Set-Content $deployment.FullName -Encoding utf8
    }

    # Check: no password, key or token may stay in a plain ConfigMap literal
    $leaks = Get-ChildItem aspirate-output -Recurse -Filter kustomization.yaml |
        Select-String -Pattern '^\s+- [A-Za-z0-9_]*(PASSWORD|PASS|SECRET|TOKEN|_KEY|Password|Secret|Token)[A-Za-z0-9_]*=' -CaseSensitive
    if ($leaks) { $leaks | ForEach-Object { Write-Warning "Plain secret in $($_.Path): $($_.Line.Split('=')[0].Trim())" }; exit 1 }
    Write-Host "Manifests are in aspirate-output, no plain secrets found."
}
finally {
    Pop-Location
}
