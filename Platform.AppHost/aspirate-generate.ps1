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
    # A Kubernetes node keeps an image by its tag: with IfNotPresent a rebuilt image under the same tag is never
    # pulled again, and the pods go on running the old code. So every build that changes the code gets a new tag.
    [string]$Tag = "v4",
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
    "--aspire-manifest", "manifest.json",           # made and fixed below, not by aspirate
    "--image-pull-policy", "IfNotPresent",          # Docker Desktop: local images are visible to the cluster
    "--container-image-tag", $Tag,
    "--output-format", "kustomize",
    "--include-dashboard", "false",
    "--secret-password", $StatePassword,
    "--parameter", "sql-password=Sq1!$(New-Secret)",  # SQL Server needs a digit, a letter case mix and a symbol
    "--parameter", "mongo-password=$(New-Secret)",
    "--parameter", "redis-password=$(New-Secret)",
    "--parameter", "messaging-password=$(New-Secret)",
    "--parameter", "keycloak-password=$(New-Secret)",
    "--parameter", "aggregator-client-secret=$(New-Secret 32)"
)
if (-not $Build) { $arguments += "--skip-build" }

Push-Location $PSScriptRoot
try {
    # Aspire 13 puts the passwords of MongoDB and RabbitMQ into their connection strings through a resource of type
    # annotated.string (the parameter encoded for a URI). aspirate 9.1 does not know that type and writes the text
    # "{messaging-password-uri-encoded.value}" instead of the password, so the services get ACCESS_REFUSED.
    # The script makes the manifest itself and points those references straight to the parameter. That is safe here:
    # New-Secret gives letters and digits only, and the URI encoding does not change them.
    dotnet run --project . --launch-profile http -- --publisher manifest --output-path manifest.json
    if ($LASTEXITCODE -ne 0) { throw "the Aspire manifest was not made" }
    $text = Get-Content manifest.json -Raw
    $manifest = $text | ConvertFrom-Json
    foreach ($resource in $manifest.resources.PSObject.Properties | Where-Object { $_.Value.type -eq "annotated.string" }) {
        $text = $text.Replace("{$($resource.Name).value}", $resource.Value.value)
        Write-Host "$($resource.Name): replaced by $($resource.Value.value)"
    }
    [IO.File]::WriteAllText((Join-Path $PWD "manifest.json"), $text, (New-Object Text.UTF8Encoding $false))

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
        $kept = @($lines | Where-Object { $_ -notmatch $sensitive })
        $item = @("- name: $name-protected", "  envs:", "  - $envFile")
        # A project already has "secretGenerator:" (its connection strings). A second key of the same name would
        # replace the first one in YAML and the connection strings would be lost, so the item joins that list.
        $at = [Array]::IndexOf($kept, "secretGenerator:")
        if ($at -lt 0) {
            $kept += @("", "secretGenerator:") + $item
        } else {
            $end = $at + 1
            while ($end -lt $kept.Count -and $kept[$end] -match '^(-|\s)') { $end++ }   # to the next top-level key
            $kept = $kept[0..($end - 1)] + $item + $(if ($end -lt $kept.Count) { $kept[$end..($kept.Count - 1)] } else { @() })
        }
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
    $duplicates = Get-ChildItem aspirate-output -Recurse -Filter kustomization.yaml |
        Where-Object { @(Select-String -Path $_.FullName -Pattern '^secretGenerator:').Count -gt 1 }
    if ($duplicates) { $duplicates | ForEach-Object { Write-Warning "Two secretGenerator keys in $($_.FullName)" }; exit 1 }
    Write-Host "Manifests are in aspirate-output, no plain secrets found."
}
finally {
    Pop-Location
}
