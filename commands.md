winget install Microsoft.DotNet.SDK.9

dotnet new sln -n ActivityTracker
dotnet new wpf -n ActivityTracker -o src\ActivityTracker -f net9.0
dotnet sln add src\ActivityTracker\ActivityTracker.csproj

dotnet build

dotnet add src\ActivityTracker package Microsoft.EntityFrameworkCore.Sqlite --version "9.0.*"
dotnet add src\ActivityTracker package Microsoft.EntityFrameworkCore.Design --version "9.0.*"

