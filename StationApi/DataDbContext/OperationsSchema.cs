using Microsoft.EntityFrameworkCore;
namespace StationApi.DataDbContext;

// Additive upgrade for existing installations. No existing tables or data are replaced.
public static class OperationsSchema
{
    public static async Task Upgrade(OperationsDbContext db)
    {
        string[] commands = [
            "CREATE TABLE IF NOT EXISTS RiderFavorites (UserId int NOT NULL, StationId varchar(255) NOT NULL, PRIMARY KEY(UserId, StationId)) CHARACTER SET utf8mb4",
            "CREATE TABLE IF NOT EXISTS RiderIssues (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, UserId int NOT NULL, StationId varchar(255) NOT NULL, Description varchar(1000) NOT NULL, Status varchar(16) NOT NULL, CreatedAt datetime(6) NOT NULL, ResolvedAt datetime(6) NULL, ResolvedBy int NULL, INDEX IX_RiderIssues_StationId_Status(StationId, Status)) CHARACTER SET utf8mb4",
            "CREATE TABLE IF NOT EXISTS TaskMeasurements (RecommendationId bigint NOT NULL PRIMARY KEY, StartedAt datetime(6) NULL, CompletedAt datetime(6) NULL, DestinationBefore int NULL, DestinationAfter int NULL, ObservedAt datetime(6) NULL)",
            "CREATE TABLE IF NOT EXISTS ForecastSamples (Id bigint NOT NULL AUTO_INCREMENT PRIMARY KEY, StationId varchar(255) NOT NULL, IssuedAt datetime(6) NOT NULL, DueAt datetime(6) NOT NULL, PredictedCount double NOT NULL, ActualCount int NULL, ObservedAt datetime(6) NULL, INDEX IX_ForecastSamples_StationId_IssuedAt(StationId, IssuedAt)) CHARACTER SET utf8mb4"
        ];
        foreach (var command in commands) await db.Database.ExecuteSqlRawAsync(command);
    }
}
