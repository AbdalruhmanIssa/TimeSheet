using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Services.Interface
{
    public interface IClientService
    {
        Task<List<ClientResponse>> GetAllAsync(ClientFilterRequest filter);
        Task<ClientResponse> GetByIdAsync(Guid clientId);
        Task<ClientResponse> CreateAsync(CreateClientRequest request);
        Task<ClientResponse> UpdateAsync(Guid clientId, UpdateClientRequest request);

        // Soft-disable / re-enable (clients are never hard-deleted).
        Task<ClientResponse> SetActiveAsync(Guid clientId, bool isActive);
    }
}
