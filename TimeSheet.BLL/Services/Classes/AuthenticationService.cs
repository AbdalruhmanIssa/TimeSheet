using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.BLL.Services.Classes
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly IUnitOfWork _uow;

        public AuthenticationService(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration configuration,
            IUnitOfWork uow)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _uow = uow;
        }

        public async Task<bool> IsUserActiveAsync(string userId, string? tokenRole)
        {
            var userRoles = _uow.Repo<IdentityUserRole<string>>().Query();
            var roles = _uow.Repo<IdentityRole>().Query();

            var state = await _uow.Repo<ApplicationUser>().Query()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.IsBlocked,
                    Role = userRoles
                        .Where(ur => ur.UserId == u.Id)
                        .Join(roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                        .FirstOrDefault()
                })
                .FirstOrDefaultAsync();

            return state is not null && !state.IsBlocked && state.Role == tokenRole;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            var user = await AuthenticateAsync(request);
            var expiresAtUtc = GetTokenExpiryUtc();

            return new LoginResponse
            {
                Token = await CreateTokenAsync(user, expiresAtUtc),
                ExpiresAtUtc = expiresAtUtc
            };
        }

        private async Task<ApplicationUser> AuthenticateAsync(LoginRequest request)
        {
            var user =
                await _userManager.FindByEmailAsync(request.EmailOrUserName)
                ?? await _userManager.FindByNameAsync(request.EmailOrUserName);

            // Don't reveal whether the user exists.
            if (user is null)
                throw new BadRequestException("Invalid email/username or password");

            if (user.IsBlocked)
                throw new ForbiddenException("Your account is blocked");

            var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

            if (result.Succeeded)
                return user;

            if (result.IsLockedOut)
                throw new ForbiddenException("Your account is locked");

            throw new BadRequestException("Invalid email/username or password");
        }

        private async Task<string> CreateTokenAsync(ApplicationUser user, DateTime expiresAtUtc)
        {
            var claims = new List<Claim>
            {
                new Claim("Id", user.Id),
                new Claim("Name", user.UserName ?? ""),
                new Claim("FullName", user.FullName ?? ""),
                new Claim("Email", user.Email ?? "")
            };

            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
                claims.Add(new Claim("Role", role));

            var jwtSection = _configuration.GetSection("jwtOptions");
            var secretBase64 = jwtSection["SecretKey"];
            if (string.IsNullOrWhiteSpace(secretBase64))
                throw new InvalidOperationException(
                    "No JWT signing key is configured ('jwtOptions:SecretKey').");

            var keyBytes = Convert.FromBase64String(secretBase64);
            var securityKey = new SymmetricSecurityKey(keyBytes);
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtSection["Issuer"],
                audience: jwtSection["Audience"],
                claims: claims,
                expires: expiresAtUtc,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private DateTime GetTokenExpiryUtc()
        {
            var jwtSection = _configuration.GetSection("jwtOptions");
            var minutes = double.TryParse(jwtSection["DurationInMinutes"], out var m) ? m : 60;
            return DateTime.UtcNow.AddMinutes(minutes);
        }
    }
}
