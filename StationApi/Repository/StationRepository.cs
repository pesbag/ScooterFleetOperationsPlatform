using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using StationApi.DataDbContext;
using StationApi.Dtos;
using StationApi.StoreDatabaseSettings;
using System.ComponentModel.DataAnnotations;
namespace StationApi.Repository;

public class StationRepository: IStationRepository
{
    private readonly IMongoCollection<BsonDocument> _collection;
    private readonly StationDbContext _sqlContext;
    private readonly string _prefix;
    public  StationRepository(IMongoDatabase collection,
        StationDbContext sqlContext, IConfiguration config)
    {
        _collection = collection.GetCollection<BsonDocument>("StationsStatus"); ;
        _sqlContext=sqlContext;
        _prefix = config["DataSource:StationPrefix"]!;
    }
    public async Task<IEnumerable<GetFiletrDto>> GetStationAsync(
    int? minAvailableBikes, int? isRenting, int? isReturning)
    {
        var filterBuilder = Builders<BsonDocument>.Filter;
        var filter = filterBuilder.Regex("Station_id", new BsonRegularExpression("^" + System.Text.RegularExpressions.Regex.Escape(_prefix)));
        if (minAvailableBikes.HasValue)
        {
            filter &= filterBuilder.Gte("Num_bikes_available", minAvailableBikes.Value);
        }
        if (isRenting.HasValue)
        {
            filter &= filterBuilder.Eq("Is_renting", isRenting.Value);
        }
        if (isReturning.HasValue)
        {
            filter &= filterBuilder.Eq("Is_returning", isReturning.Value);
        }
        var mongoDocs = await _collection.Find(filter).ToListAsync();

        if (!mongoDocs.Any())
        {
            return Enumerable.Empty<GetFiletrDto>();
        }

        // שלב 2: חילוץ מזהי התחנות שנמצאו
        var stationIds = mongoDocs
            .Select(d => d.GetValue("Station_id", d.GetValue("station_id", string.Empty)).AsString)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        // שליפה יעילה מ-MySQL במכה אחת (WHERE IN)
        var sqlStations = await _sqlContext.stationInformation
            .AsNoTracking()
            .Where(s => stationIds.Contains(s.Station_id))
            .ToDictionaryAsync(s => s.Station_id);

        // שלב 3: מיזוג הנתונים ל-AllDataDto
        var result = new List<GetFiletrDto>();

        foreach (var doc in mongoDocs)
        {
            string id = doc.GetValue("Station_id", doc.GetValue("station_id", string.Empty)).AsString;

            sqlStations.TryGetValue(id, out var sqlInfo);

            result.Add(new GetFiletrDto
            {
                Station_id = id,
                Name = sqlInfo?.Name ?? string.Empty,
                Lat = sqlInfo?.Lat ?? 0,
                Lon = sqlInfo?.Lon ?? 0,
                Capacity = sqlInfo?.Capacity ?? 0,

                Num_bikes_available = Number(doc, "Num_bikes_available", "num_bikes_available"),
                //Nun_vehicles_available = Number(doc, "Nun_vehicles_available", "num_vehicles_available"),
                Num_docs_available = Number(doc, "Num_docs_available", "num_docks_available"),
                Is_renting = Number(doc, "Is_renting", "is_renting"),
                Is_returning = Number(doc, "Is_returning", "is_returning"),
                //Last_reported = doc.GetValue("Last_reported", doc.GetValue("last_reported", 0L)).ToInt64()
            });
        }

        return result;
    }
    public async Task<AllDataDto?> GetAllDataByIdASync(string id)
    {
        if (!id.StartsWith(_prefix, StringComparison.Ordinal)) return null;
        var sqlResult = await _sqlContext
            .stationInformation
            .AsNoTracking()
            .FirstOrDefaultAsync(x=>x.Station_id==id);

        var filter = Builders<BsonDocument>.Filter.Eq("Station_id", id);
        var mongoDoc = await _collection.Find(filter).FirstOrDefaultAsync();
        if (sqlResult is null || mongoDoc is null)
        {
            return null;
        }
        return new AllDataDto
        {
            Station_id = id,

            Name = sqlResult.Name ?? string.Empty,
            Lat = sqlResult.Lat,
            Lon = sqlResult.Lon,
            Capacity = sqlResult.Capacity,

            Num_bikes_available = Number(mongoDoc, "Num_bikes_available", "num_bikes_available"),
            Nun_vehicles_available = Number(mongoDoc, "Nun_vehicles_available", "num_vehicles_available"),
            Num_docs_available = Number(mongoDoc, "Num_docs_available", "num_docks_available"),
            Is_renting = Number(mongoDoc, "Is_renting", "is_renting"),
            Is_returning = Number(mongoDoc, "Is_returning", "is_returning"),
            Last_reported = Timestamp(mongoDoc)
        };
    }
    private static int? Number(BsonDocument doc, string key, string fallback)
    {
        var value = doc.GetValue(key, doc.GetValue(fallback, BsonNull.Value));
        if (value.IsBoolean) return value.AsBoolean ? 1 : 0;
        return (value.IsInt32 || value.IsInt64) && value.ToInt64() is >= int.MinValue and <= int.MaxValue
            ? value.ToInt32() : null;
    }
    private static long? Timestamp(BsonDocument doc)
    {
        var value = doc.GetValue("Last_reported", doc.GetValue("last_reported", BsonNull.Value));
        return value.IsInt64 || value.IsInt32 ? value.ToInt64() : null;
    }
}
