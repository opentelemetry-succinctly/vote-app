FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
WORKDIR /src
COPY ["vote-processor/vote-processor.csproj", "vote-processor/"]
COPY ["../common/common.csproj", "common/"]
COPY ["Directory.Build.props", "./"]
RUN dotnet restore "vote-processor/vote-processor.csproj"
COPY . .
WORKDIR "/src/vote-processor"
RUN dotnet build "vote-processor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "vote-processor.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "vote-processor.dll"]