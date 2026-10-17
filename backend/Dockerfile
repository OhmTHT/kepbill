# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ReceiptReader.slnx ./
COPY src/ReceiptReader.Domain/*.csproj src/ReceiptReader.Domain/
COPY src/ReceiptReader.Application/*.csproj src/ReceiptReader.Application/
COPY src/ReceiptReader.Infrastructure/*.csproj src/ReceiptReader.Infrastructure/
COPY src/ReceiptReader.Api/*.csproj src/ReceiptReader.Api/
RUN dotnet restore src/ReceiptReader.Api/ReceiptReader.Api.csproj
COPY src/ src/
RUN dotnet publish src/ReceiptReader.Api/ReceiptReader.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_URLS=http://+:8080 \
    Storage__RootPath=/data/uploads
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "ReceiptReader.Api.dll"]
