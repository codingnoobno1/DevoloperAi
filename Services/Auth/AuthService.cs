using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Syncro.Desktop.Services.Auth
{
    public class AuthService
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://project-syncroo.netlify.app";

        public string? Token { get; private set; }
        public string? CurrentUserId { get; private set; }

        public AuthService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<(bool success, string message)> Register(RegisterModel model)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/register", model);
                
                if (response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    if (response.IsSuccessStatusCode && result != null)
                    {
                        Token = result.Token;
                        CurrentUserId = result.UserId;
                        return (true, result.Message ?? "Registration successful");
                    }
                    return (false, result?.Message ?? $"Registration failed with status {response.StatusCode}");
                }
                else
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return (false, $"Server returned non-JSON response: {response.StatusCode}. Make sure the API URL is correct.");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Connection error: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> Login(string email, string password)
        {
            try
            {
                // Use the mobile-friendly endpoint that returns a JWT token directly
                var payload = new { email, password };
                var response = await _httpClient.PostAsJsonAsync($"{BaseUrl}/api/mobile/login", payload);
                
                if (response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    if (response.IsSuccessStatusCode && result != null)
                    {
                        Token = result.Token;
                        CurrentUserId = result.User?.Id ?? result.UserId;
                        return (true, "Login successful");
                    }
                    return (false, result?.Message ?? $"Login failed: {response.StatusCode}");
                }
                
                return (false, $"Login failed: {response.StatusCode}. Check credentials or API endpoint.");
            }
            catch (Exception ex)
            {
                return (false, $"Connection error: {ex.Message}");
            }
        }

        public void Logout()
        {
            Token = null;
            CurrentUserId = null;
        }
    }

    public class RegisterModel
    {
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string UniversityName { get; set; } = "";
        public string EnrollmentNumber { get; set; } = "";
        public string TechStackPreference { get; set; } = "";
        public string Password { get; set; } = "";
        public string Role { get; set; } = "normal_user";
    }

    public class AuthResponse
    {
        public string Message { get; set; } = "";
        public string? UserId { get; set; } // From register endpoint
        public string? Token { get; set; }
        public UserInfo? User { get; set; } // From mobile login endpoint
    }

    public class UserInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
    }
}
