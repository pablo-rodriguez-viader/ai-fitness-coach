FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/AIFitnessCoach.Domain/AIFitnessCoach.Domain.csproj src/AIFitnessCoach.Domain/
COPY src/AIFitnessCoach.Application/AIFitnessCoach.Application.csproj src/AIFitnessCoach.Application/
COPY src/AIFitnessCoach.AI/AIFitnessCoach.AI.csproj src/AIFitnessCoach.AI/
COPY src/AIFitnessCoach.Infrastructure/AIFitnessCoach.Infrastructure.csproj src/AIFitnessCoach.Infrastructure/
COPY src/AIFitnessCoach.Api/AIFitnessCoach.Api.csproj src/AIFitnessCoach.Api/
RUN dotnet restore src/AIFitnessCoach.Api/AIFitnessCoach.Api.csproj
COPY src/ src/
RUN dotnet publish src/AIFitnessCoach.Api/AIFitnessCoach.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "AIFitnessCoach.Api.dll"]
