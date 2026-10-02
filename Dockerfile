# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Documented.sln", "./"]
COPY ["src/Documented.Web/Documented.Web.csproj", "src/Documented.Web/"]
RUN dotnet restore "Documented.sln"

COPY . .
RUN dotnet publish "src/Documented.Web/Documented.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Documented.Web.dll"]
