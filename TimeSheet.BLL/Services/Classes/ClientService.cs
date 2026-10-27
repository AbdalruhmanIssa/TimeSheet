using Mapster;
using Microsoft.EntityFrameworkCore;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Extensions;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.BLL.Services.Classes
{
    public class ClientService : IClientService
    {
        private readonly IUnitOfWork _uow;

        public ClientService(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task<PagedResponse<ClientResponse>> GetAllAsync(ClientFilterRequest filter)
        {
            var query = _uow.Repo<Client>().Query();

            if (!filter.IncludeInactive)
                query = query.Where(c => c.IsActive);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search.Trim();
                query = query.Where(c =>
                    c.Name.Contains(search) ||
                    (c.ContactName != null && c.ContactName.Contains(search)));
            }

            return await query
                .OrderBy(c => c.Name) // unique
                .ProjectToType<ClientResponse>()
                .ToPagedAsync(filter);
        }

        public async Task<ClientResponse> GetByIdAsync(Guid clientId)
        {
            var client = await FindClientAsync(clientId);
            return client.Adapt<ClientResponse>();
        }

        public async Task<ClientResponse> CreateAsync(CreateClientRequest request)
        {
            var name = request.Name.Trim();
            await EnsureNameIsFreeAsync(name, excludeId: null);

            var client = new Client
            {
                Name = name,
                ContactName = NullIfBlank(request.ContactName),
                ContactEmail = NullIfBlank(request.ContactEmail),
                Phone = NullIfBlank(request.Phone),
                IsActive = true
            };

            await _uow.Repo<Client>().AddAsync(client);
            await _uow.SaveChangesAsync();

            return client.Adapt<ClientResponse>();
        }

        public async Task<ClientResponse> UpdateAsync(Guid clientId, UpdateClientRequest request)
        {
            var client = await FindClientAsync(clientId);

            var name = request.Name.Trim();
            await EnsureNameIsFreeAsync(name, excludeId: clientId);

            client.Name = name;
            client.ContactName = NullIfBlank(request.ContactName);
            client.ContactEmail = NullIfBlank(request.ContactEmail);
            client.Phone = NullIfBlank(request.Phone);

            await _uow.SaveChangesAsync();

            return client.Adapt<ClientResponse>();
        }

        public async Task<ClientResponse> SetActiveAsync(Guid clientId, bool isActive)
        {
            var client = await FindClientAsync(clientId);

            if (client.IsActive != isActive)
            {
                client.IsActive = isActive;
                await _uow.SaveChangesAsync();
            }

            return client.Adapt<ClientResponse>();
        }

        // ------------------------------------------------------------------

        private async Task<Client> FindClientAsync(Guid clientId)
        {
            return await _uow.Repo<Client>().Query().FirstOrDefaultAsync(c => c.Id == clientId)
                   ?? throw new NotFoundException("Client not found.");
        }

        // Checked in code for a clear message; the unique index is the safety net.
        private async Task EnsureNameIsFreeAsync(string name, Guid? excludeId)
        {
            var taken = await _uow.Repo<Client>().Query()
                .AnyAsync(c => c.Name == name && (excludeId == null || c.Id != excludeId));

            if (taken)
                throw new BadRequestException("A client with this name already exists.");
        }

        private static string? NullIfBlank(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
