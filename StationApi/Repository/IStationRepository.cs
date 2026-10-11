using StationApi.Dtos;

namespace StationApi.Repository;

public interface IStationRepository
{
    Task<IEnumerable<GetFiletrDto>> GetStationAsync(
        int? minAvailableBikes, int? isRenting, int? isReturning);

    Task<AllDataDto?> GetAllDataByIdAsync(string id);
}
