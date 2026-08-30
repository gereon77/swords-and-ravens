# Mirrors build_and_place_game_client_into_django.sh, but targets the ASP.NET Core rewrite
# (agot-bg-website-dotnet) instead of Django — see agot-bg-website-dotnet/MIGRATION_PLAN.md §8.
#
# This script builds the game client and places it into the .NET app. It is not used to build the
# production artifact ("website.Dockerfile"/"agot-bg-website-dotnet/agot-bg-website/Dockerfile"
# takes care of that for production); it's only meant for local development, to check that the
# integration between the game server and the website functions properly.

$ErrorActionPreference = "Stop"

Write-Host "---> Building the game client"
Push-Location agot-bg-game-server
yarn install
if ($LASTEXITCODE -ne 0) { throw "yarn install failed" }
yarn run build-local-client
if ($LASTEXITCODE -ne 0) { throw "yarn run build-local-client failed" }
Pop-Location

Write-Host "---> Placing the static files of the game client into the .NET app"
$dest = "agot-bg-website-dotnet/agot-bg-website/wwwroot/static_game"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Get-ChildItem -Path "agot-bg-game-server/dist" -Exclude "index.html" | Copy-Item -Destination $dest -Recurse -Force

$templateDir = "agot-bg-website-dotnet/agot-bg-website/GameClientTemplates"
New-Item -ItemType Directory -Force -Path $templateDir | Out-Null
Copy-Item -Force "agot-bg-game-server/dist/index.html" "$templateDir/play.html"

Write-Host "---> Done. Run 'dotnet run --project agot-bg-website-dotnet/agot-bg-website' to serve it."
