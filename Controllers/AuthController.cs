using Microsoft.AspNetCore.Mvc;
using Syntera.WMS.API.Services;

namespace Syntera.WMS.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { success = false, message = "Username and password are required." });

            var result = await _authService.LoginAsync(request.Username, request.Password);

            if (result == null)
                return Unauthorized(new { success = false, message = "Invalid username or password." });

            return Ok(new
            {
                success = true,
                data = new
                {
                    token = result.Token,
                    userId = result.UserId,
                    username = result.Username,
                    fullName = result.FullName,
                    role = result.Role
                }
            });
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
