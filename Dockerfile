FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Install Python and dependencies for your Python scripts
RUN apt-get update && apt-get install -y python3 python3-pip python3-venv
RUN python3 -m pip install --upgrade pip

# Copy Python requirements if you have them
COPY requirements.txt* ./
RUN if [ -f requirements.txt ]; then pip3 install -r requirements.txt; fi

# Copy project files
COPY *.csproj ./
RUN dotnet restore

# Copy everything else
COPY . ./
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Install Python in final image too
RUN apt-get update && apt-get install -y python3 python3-pip python3-venv
RUN python3 -m pip install --upgrade pip

# Copy Python requirements and install
COPY requirements.txt* ./
RUN if [ -f requirements.txt ]; then pip3 install -r requirements.txt; fi

COPY --from=publish /app/publish .

# Make sure Python scripts are executable
RUN chmod +x *.py

ENTRYPOINT ["dotnet", "AccessFormServer.dll"]
