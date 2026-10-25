using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.PL.Controllers.Director
{
    [ApiController]
    [Area("Director")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Director)]
    public class ClientsController : ControllerBase
    {
        private readonly IClientService _clientService;

        public ClientsController(IClientService clientService)
        {
            _clientService = clientService;
        }

        // GET api/Director/Clients?search=acme&includeInactive=true
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] ClientFilterRequest filter)
        {
            var clients = await _clientService.GetAllAsync(filter);
            return Ok(clients);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var client = await _clientService.GetByIdAsync(id);
            return Ok(client);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateClientRequest request)
        {
            var client = await _clientService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, UpdateClientRequest request)
        {
            var client = await _clientService.UpdateAsync(id, request);
            return Ok(client);
        }

        // Soft-disable: clients are never hard-deleted.
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Deactivate(Guid id)
        {
            var client = await _clientService.SetActiveAsync(id, isActive: false);
            return Ok(client);
        }

        [HttpPatch("{id:guid}/activate")]
        public async Task<IActionResult> Activate(Guid id)
        {
            var client = await _clientService.SetActiveAsync(id, isActive: true);
            return Ok(client);
        }
    }
}
