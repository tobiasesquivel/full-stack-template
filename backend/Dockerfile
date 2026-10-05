FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY ["TemplateApp.csproj", "./"]
RUN dotnet restore "TemplateApp.csproj"
COPY . .
RUN dotnet publish "TemplateApp.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
WORKDIR /app
USER $APP_UID
COPY --from=build --chown=app:app /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TemplateApp.dll"]
