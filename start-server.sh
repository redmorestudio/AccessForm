#!/bin/bash

# Kill any existing processes
pkill -f dotnet || true
lsof -i :5008 | grep -v COMMAND | awk '{print $2}' | xargs kill -9 2>/dev/null || true

# Clean build artifacts
echo "Cleaning build artifacts..."
rm -rf bin obj

# Set environment
export ASPNETCORE_URLS="http://localhost:5008"
export ASPNETCORE_ENVIRONMENT="Development"
export DOTNET_CLI_TELEMETRY_OPTOUT=1

# Run the server
echo "Starting AccessForm Server on http://localhost:5008"
/usr/local/share/dotnet/dotnet run --project AccessFormServer.csproj --urls "http://localhost:5008"
