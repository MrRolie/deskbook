# The deskbook image: built with the pinned .NET SDK image, run on the pinned ASP.NET runtime image (smaller, no
# compiler), as the image's unprivileged "app" user. One image holds both programs:
#   dotnet /app/cli/deskbook.dll ...        the command line (the default entry point)
#   dotnet /app/web/Deskbook.Web.dll        the read-only page
FROM mcr.microsoft.com/dotnet/sdk@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src
COPY nuget.config Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Deskbook.Cli -c Release -o /app/cli \
 && dotnet publish src/Deskbook.Web -c Release -o /app/web

FROM mcr.microsoft.com/dotnet/aspnet@sha256:aac50ceef10391e78a899e3d2ccd00bbafa2dcb56ccf4586cbccf00edb84fc21
WORKDIR /app
COPY --from=build /app /app
USER app
ENTRYPOINT ["dotnet", "/app/cli/deskbook.dll"]
CMD ["help"]
