using FluentAssertions;
using Moq;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.Tests.Services
{
    public class ClientServiceTests
    {
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly ClientService _sut;

        public ClientServiceTests()
        {
            _sut = new ClientService(_uow.Object);
        }

        // ---------------------------------------------------------------- Get

        [Fact]
        public async Task GetAllAsync_ByDefault_HidesInactive_OrderedByName()
        {
            _uow.SetupRepo(new List<Client>
            {
                NewClient("Northwind"),
                NewClient("Fabrikam", isActive: false),
                NewClient("Acme")
            });

            var result = (await _sut.GetAllAsync(new ClientFilterRequest())).Items;

            result.Select(c => c.Name).Should().Equal("Acme", "Northwind");
        }

        [Fact]
        public async Task GetAllAsync_WithIncludeInactive_ReturnsAll()
        {
            _uow.SetupRepo(new List<Client> { NewClient("Acme"), NewClient("Fabrikam", isActive: false) });

            var result = (await _sut.GetAllAsync(new ClientFilterRequest { IncludeInactive = true })).Items;

            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetByIdAsync_WhenMissing_ThrowsNotFound()
        {
            _uow.SetupRepo(new List<Client>());

            var act = () => _sut.GetByIdAsync(Guid.NewGuid());

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Client not found.");
        }

        // ---------------------------------------------------------------- Create

        [Fact]
        public async Task CreateAsync_WithExistingName_ThrowsBadRequest()
        {
            var repo = _uow.SetupRepo(new List<Client> { NewClient("Acme") });

            var act = () => _sut.CreateAsync(new CreateClientRequest { Name = "  Acme  " });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("A client with this name already exists.");
            repo.Verify(r => r.AddAsync(It.IsAny<Client>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_TrimsValues_BlankToNull_AndSaves()
        {
            var repo = _uow.SetupRepo(new List<Client>());

            var result = await _sut.CreateAsync(new CreateClientRequest
            {
                Name = " Acme ",
                ContactName = " Sara ",
                ContactEmail = "   ",
                Phone = null
            });

            result.Name.Should().Be("Acme");
            result.IsActive.Should().BeTrue();
            repo.Verify(r => r.AddAsync(
                It.Is<Client>(c => c.Name == "Acme" && c.ContactName == "Sara" && c.ContactEmail == null),
                It.IsAny<CancellationToken>()), Times.Once);
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------------------------------------------------------------- Update

        [Fact]
        public async Task UpdateAsync_RenamingToAnotherClientsName_ThrowsBadRequest()
        {
            var acme = NewClient("Acme");
            _uow.SetupRepo(new List<Client> { acme, NewClient("Northwind") });

            var act = () => _sut.UpdateAsync(acme.Id, new UpdateClientRequest { Name = "Northwind" });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("A client with this name already exists.");
        }

        [Fact]
        public async Task UpdateAsync_KeepingOwnName_Succeeds()
        {
            var acme = NewClient("Acme");
            _uow.SetupRepo(new List<Client> { acme });

            var result = await _sut.UpdateAsync(acme.Id, new UpdateClientRequest { Name = "Acme", ContactName = "Omar" });

            result.ContactName.Should().Be("Omar");
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------------------------------------------------------------- Activate / deactivate

        [Fact]
        public async Task SetActiveAsync_Deactivates_AndSaves()
        {
            var acme = NewClient("Acme");
            _uow.SetupRepo(new List<Client> { acme });

            var result = await _sut.SetActiveAsync(acme.Id, isActive: false);

            result.IsActive.Should().BeFalse();
            acme.IsActive.Should().BeFalse();
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetActiveAsync_WhenAlreadyInThatState_DoesNotSave()
        {
            var acme = NewClient("Acme");
            _uow.SetupRepo(new List<Client> { acme });

            await _sut.SetActiveAsync(acme.Id, isActive: true);

            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        // ------------------------------------------------------------------

        private static Client NewClient(string name, bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = isActive
        };
    }
}
