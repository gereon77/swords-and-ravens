#!/bin/bash

# Mirrors build_and_place_game_client_into_django.sh, but targets the ASP.NET Core rewrite
# (agot-bg-website-dotnet) instead of Django — see agot-bg-website-dotnet/MIGRATION_PLAN.md §8.
#
# This script builds the game client and places it into the .NET app. It is not used to build the
# production artifact ("website.Dockerfile"/"agot-bg-website-dotnet/agot-bg-website/Dockerfile"
# takes care of that for production); it's only meant for local development, to check that the
# integration between the game server and the website functions properly.

set -e

echo "---> Building the game client"
cd agot-bg-game-server
yarn install
yarn run build-local-client
cd ..

echo "---> Placing the static files of the game client into the .NET app"
dest="agot-bg-website-dotnet/agot-bg-website/wwwroot/static_game"
mkdir -p "$dest"
find agot-bg-game-server/dist -mindepth 1 -maxdepth 1 ! -name "index.html" -exec cp -r {} "$dest/" \;

template_dir="agot-bg-website-dotnet/agot-bg-website/GameClientTemplates"
mkdir -p "$template_dir"
cp agot-bg-game-server/dist/index.html "$template_dir/play.html"

echo "---> Done. Run 'dotnet run --project agot-bg-website-dotnet/agot-bg-website' to serve it."
