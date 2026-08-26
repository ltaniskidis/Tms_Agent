# Base image for running the app
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# SDK image for compiling the app
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore dependencies
COPY ["Tms.CentralManagement/Tms.CentralManagement.csproj", "Tms.CentralManagement/"]
COPY ["Tms.Shared/Tms.Shared.csproj", "Tms.Shared/"]
RUN dotnet restore "Tms.CentralManagement/Tms.CentralManagement.csproj"

# Copy the rest of the source code and build
COPY . .
WORKDIR "/src/Tms.CentralManagement"
RUN dotnet build "Tms.CentralManagement.csproj" -c Release -o /app/build

# Publish the app
FROM build AS publish
RUN dotnet publish "Tms.CentralManagement.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create directory for SQLite database mount
RUN mkdir -p /app/data

ENTRYPOINT ["dotnet", "Tms.CentralManagement.dll"]
