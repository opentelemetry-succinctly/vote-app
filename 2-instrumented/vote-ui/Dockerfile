FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine AS base
WORKDIR /app
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine AS build
WORKDIR /src
COPY ["vote-ui/vote-ui.csproj", "vote-ui/"]
COPY ["common/common.csproj", "common/"]
COPY ["Directory.Build.props", "./"]
RUN dotnet restore "vote-ui/vote-ui.csproj"
COPY . .
WORKDIR "/src/vote-ui"
RUN dotnet build "vote-ui.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "vote-ui.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "vote-ui.dll"]