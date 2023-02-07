FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine AS base
WORKDIR /app
EXPOSE 8081

ENV ASPNETCORE_URLS=http://+:8081

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
WORKDIR /src
COPY ["vote-data/vote-data.csproj", "vote-data/"]
COPY ["../common/common.csproj", "common/"]
COPY ["Directory.Build.props", "./"]
RUN dotnet restore "vote-data/vote-data.csproj"
COPY . .
WORKDIR "/src/vote-data"
RUN dotnet build "vote-data.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "vote-data.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "vote-data.dll"]