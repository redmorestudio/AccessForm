#!/bin/bash
set -e

echo "Starting AccessForm Server..."
echo "Port: ${PORT:-5000}"
echo "Environment: ${ASPNETCORE_ENVIRONMENT:-Production}"

# Set the URLs to bind to the PORT environment variable
export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-5000}"

# Start the application
exec dotnet AccessFormServer.dll
