# Builds the BuzzerGames web app into a small image that Railway can run.
# Two stages: a big "build" image that has the .NET SDK, and a small "final" image
# that only has what's needed to run the finished app.

# ---------- Stage 1: build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Now copy the real source code and publish a release build (this restores packages too,
# so the framework's own files, like blazor.web.js, are definitely part of the build).
COPY src/ src/
RUN dotnet publish src/BuzzerGames.Web/BuzzerGames.Web.csproj -c Release -o /app/publish

# Diagnostic: shows up in the Railway build log, so we can see the Blazor script made it in.
RUN echo "--- blazor.web in publish output ---" && grep -o '"Route": *"[^"]*blazor.web[^"]*"' /app/publish/*.endpoints.json | head -5 || true

# ---------- Stage 2: run ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

# Railway sets a PORT variable when it starts the container, and Program.cs listens on it.
# If you run this image on your own computer, there is no PORT, so the app falls back to 8080.
EXPOSE 8080

ENTRYPOINT ["dotnet", "BuzzerGames.Web.dll"]
