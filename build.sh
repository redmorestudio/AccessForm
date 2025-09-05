#!/bin/bash
set -e

echo "Installing .NET dependencies..."
dotnet restore

echo "Building application..."
dotnet build --configuration Release --no-restore

echo "Build completed successfully!"
