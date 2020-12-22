FROM mcr.microsoft.com/dotnet/sdk:5.0
COPY . /app
WORKDIR /app
EXPOSE 8080
EXPOSE 9000
CMD ["dotnet", "fsi", "server.fsx"]