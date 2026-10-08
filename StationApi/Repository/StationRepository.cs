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
    public  StationRepository(IMongoDatabase collection,
        StationDbContext sqlContext)
    {
        _collection = collection.GetCollection<BsonDocument>("StationsStatus"); ;
        _sqlContext=sqlContext;
    }
    public async Task<IEnumerable<GetFiletrDto>> GetStationAsync(
    int? minAvailableBikes, int? isRenting, int? isReturning)
    {
        var filterBuilder = Builders<BsonDocument>.Filter;
        var filter = filterBuilder.Empty;
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

                Num_bikes_available = doc.GetValue("Num_bikes_available", doc.GetValue("num_bikes_available", 0)).ToInt32(),
                //Nun_vehicles_available = doc.GetValue("Nun_vehicles_available", doc.GetValue("num_vehicles_available", 0)).ToInt32(),
                Num_docs_available = doc.GetValue("Num_docs_available", doc.GetValue("num_docks_available", 0)).ToInt32(),
                Is_renting = doc.GetValue("Is_renting", doc.GetValue("is_renting", 0)).ToInt32(),
                Is_returning = doc.GetValue("Is_returning", doc.GetValue("is_returning", 0)).ToInt32(),
                //Last_reported = doc.GetValue("Last_reported", doc.GetValue("last_reported", 0L)).ToInt64()
            });
        }

        return result;
    }
    public async Task<AllDataDto?> GetAllDataByIdASync(string id)
    {
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

            Num_bikes_available = mongoDoc.GetValue("Num_bikes_available", mongoDoc.GetValue("num_bikes_available", 0)).ToInt32(),
            Nun_vehicles_available = mongoDoc.GetValue("Nun_vehicles_available", mongoDoc.GetValue("num_vehicles_available", 0)).ToInt32(),
            Num_docs_available = mongoDoc.GetValue("Num_docs_available", mongoDoc.GetValue("num_docks_available", 0)).ToInt32(),
            Is_renting = mongoDoc.GetValue("Is_renting", mongoDoc.GetValue("is_renting", 0)).ToInt32(),
            Is_returning = mongoDoc.GetValue("Is_returning", mongoDoc.GetValue("is_returning", 0)).ToInt32(),
            Last_reported = mongoDoc.GetValue("Last_reported", mongoDoc.GetValue("last_reported", 0L)).ToInt64()
        };
    }
}
