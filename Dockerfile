# osnovna slika za zagon
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# slika za izgradnjo
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["DockerNaloga/DockerNaloga.csproj", "DockerNaloga/"]
RUN dotnet restore "DockerNaloga/DockerNaloga.csproj"
COPY . .
WORKDIR "/src/DockerNaloga"
RUN dotnet build "DockerNaloga.csproj" -c Release -o /app/build

# objava projekta
FROM build AS publish
RUN dotnet publish "DockerNaloga.csproj" -c Release -o /app/publish /p:UseAppHost=false

# končna slika
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "DockerNaloga.dll"]
