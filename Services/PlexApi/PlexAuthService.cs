using PlexBot.Core.Exceptions;
using PlexBot.Utils.Http;
using PlexBot.Utils;

namespace PlexBot.Services.PlexApi;

/// <summary>
/// Handles authentication with the Plex API.
/// This service manages the Plex authentication flow, including token generation,
/// verification, and storage. It provides the foundation for all other Plex API
/// interactions by ensuring proper authentication.
/// </summary>
public class PlexAuthService : IPlexAuthService
{
    private readonly HttpClientWrapper _httpClient;
    private readonly string _clientIdentifierKey;
    private readonly string _plexAppName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlexAuthService"/> class.
    /// Sets up the service with necessary configuration values and HTTP clients.
    /// </summary>
    /// <param name="httpClientFactory">Factory for creating HTTP clients</param>
    public PlexAuthService(HttpClient httpClient)
    {
        _httpClient = new HttpClientWrapper(httpClient, "PlexAuth");
        _clientIdentifierKey = EnvConfig.Get("PLEX_CLIENT_IDENTIFIER", Guid.NewGuid().ToString());
        _plexAppName = EnvConfig.Get("PLEX_APP_NAME", "PlexBot");

        Logs.Debug($"PlexAuthService initialized with client ID: {_clientIdentifierKey}");
    }

    /// <inheritdoc />
    public async Task<bool> VerifyStoredAccessTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try
        {
            Logs.Debug("Verifying Plex access token");

            string requestUrl = "https://plex.tv/api/v2/user";
            var headers = new Dictionary<string, string>
            {
                ["accept"] = "application/json",
                ["X-Plex-Product"] = _plexAppName,
                ["X-Plex-Client-Identifier"] = _clientIdentifierKey,
                ["X-Plex-Token"] = accessToken
            };

            try
            {
                // We don't care about the response content, just that the request succeeds
                await _httpClient.SendRequestForStringAsync(HttpMethod.Get, requestUrl, null, headers, cancellationToken);
                Logs.Debug("Plex access token verified successfully");
                return true;
            }
            catch (PlexApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                Logs.Warning("Plex access token verification failed: Unauthorized");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logs.Warning($"Error verifying Plex access token: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<(int pinId, string pinCode)> GeneratePinAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Logs.Debug("Generating Plex PIN for authentication");

            string requestUrl = "https://plex.tv/api/v2/pins";
            var headers = new Dictionary<string, string>
            {
                ["accept"] = "application/json",
                ["X-Plex-Product"] = _plexAppName,
                ["X-Plex-Client-Identifier"] = _clientIdentifierKey,
                ["Content-Type"] = "application/x-www-form-urlencoded"
            };

            using var content = new StringContent("strong=true", Encoding.UTF8, "application/x-www-form-urlencoded");

            var response = await _httpClient.SendRequestForStringAsync(HttpMethod.Post, requestUrl, content, headers, cancellationToken);

            JsonDocument jsonDoc = JsonDocument.Parse(response);
            int pinId = jsonDoc.RootElement.GetProperty("id").GetInt32();
            string? pinCode = jsonDoc.RootElement.GetProperty("code").GetString() ??
                throw new AuthenticationException("Pin code was not returned by the server.", "PinGeneration");

            Logs.Info($"Generated Plex PIN: ID = {pinId}, Code = {pinCode}");
            return (pinId, pinCode);
        }
        catch (PlexApiException ex)
        {
            throw new AuthenticationException($"Failed to generate PIN: {ex.Message}", "PinGeneration", ex);
        }
        catch (Exception ex) when (ex is not AuthenticationException)
        {
            throw new AuthenticationException($"Unexpected error generating PIN: {ex.Message}", "PinGeneration", ex);
        }
    }

    /// <inheritdoc />
    public string ConstructAuthAppUrl(string pinCode, string forwardUrl)
    {
        Logs.Debug($"Constructing Plex auth URL for PIN: {pinCode}");

        string authUrl = $"https://app.plex.tv/auth#?clientID={_clientIdentifierKey}&code={pinCode}" +
            $"&context%5Bdevice%5D%5Bproduct%5D={Uri.EscapeDataString(_plexAppName)}&forwardUrl={Uri.EscapeDataString(forwardUrl)}";

        Logs.Info($"Plex authentication URL: {authUrl}");
        return authUrl;
    }

    /// <inheritdoc />
    public async Task<string?> CheckPinAsync(int pinId, CancellationToken cancellationToken = default)
    {
        try
        {
            Logs.Debug($"Checking status of Plex PIN: {pinId}");

            string requestUrl = $"https://plex.tv/api/v2/pins/{pinId}";
            var headers = new Dictionary<string, string>
            {
                ["accept"] = "application/json",
                ["X-Plex-Client-Identifier"] = _clientIdentifierKey
            };

            var response = await _httpClient.SendRequestForStringAsync(HttpMethod.Get, requestUrl, null, headers, cancellationToken);

            JsonDocument jsonDoc = JsonDocument.Parse(response);
            if (jsonDoc.RootElement.TryGetProperty("authToken", out JsonElement authTokenProperty))
            {
                string? authToken = authTokenProperty.GetString();
                if (!string.IsNullOrEmpty(authToken))
                {
                    Logs.Info("Successfully obtained Plex authentication token");
                    return authToken;
                }
            }

            Logs.Debug("Plex PIN not yet authorized");
            return null; // Not yet authorized
        }
        catch (PlexApiException ex)
        {
            Logs.Warning($"Error checking PIN status: {ex.Message}");
            return null; // Treat API errors as "not yet authorized"
        }
        catch (Exception ex)
        {
            Logs.Error($"Unexpected error checking PIN status: {ex.Message}");
            throw new AuthenticationException($"Failed to check PIN status: {ex.Message}", "PinCheck", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // First check for an existing token in environment variables
        string accessToken = EnvConfig.Get("PLEX_TOKEN", "");

        if (!string.IsNullOrEmpty(accessToken))
        {
            // Verify the token is still valid
            if (await VerifyStoredAccessTokenAsync(accessToken, cancellationToken))
            {
                Logs.Info("Using existing Plex token from configuration");
                return accessToken;
            }

            Logs.Warning("Stored Plex token is invalid, generating a new one");
        }
        else
        {
            Logs.Info("No Plex token found in configuration, initiating authentication flow");
        }
        // Generate a new token through the auth flow
        (int pinId, string pinCode) = await GeneratePinAsync(cancellationToken);

        // Construct the auth URL (this would be shown to the user in a real scenario)
        string authUrl = ConstructAuthAppUrl(pinCode, "http://app.plex.tv");

        // In a real bot scenario, you would send this URL to the user and ask them to visit it
        Logs.Info($"Please authenticate at this URL: {authUrl}");

        // Poll for authentication completion
        string? newAccessToken = null;
        int attempts = 0;
        int maxAttempts = 60; // Poll for up to 5 minutes (60 attempts at 5-second intervals)

        while (newAccessToken == null && attempts < maxAttempts)
        {
            attempts++;
            await Task.Delay(5000, cancellationToken); // Wait 5 seconds between checks

            try
            {
                newAccessToken = await CheckPinAsync(pinId, cancellationToken);
            }
            catch (Exception ex)
            {
                Logs.Warning($"Error checking PIN status (attempt {attempts}): {ex.Message}");
            }

            if (newAccessToken != null)
            {
                break;
            }

            if (attempts % 12 == 0) // Log every minute
            {
                Logs.Info($"Waiting for Plex authentication... ({attempts / 12} minutes elapsed)");
            }
        }

        if (newAccessToken == null)
        {
            throw new AuthenticationException("Plex authentication timed out. Please try again.", "PinCheck");
        }

        // Store the new token
        Logs.Info("Successfully authenticated with Plex. Saving token to configuration.");
        EnvConfig.Set("PLEX_TOKEN", newAccessToken);

        try
        {
            // Attempt to save the token to the .env file for persistence
            SaveTokenToEnvFile(newAccessToken);
        }
        catch (Exception ex)
        {
            // Not critical, just log it
            Logs.Warning($"Failed to save token to .env file: {ex.Message}");
        }

        return newAccessToken;
    }

    /// <summary>
    /// Saves a Plex token to the .env file for persistence across restarts.
    /// This is a helper method to update the token in the .env file when a new one is generated.
    /// </summary>
    /// <param name="token">The token to save</param>
    private void SaveTokenToEnvFile(string token)
    {
        try
        {
            string envFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");

            if (File.Exists(envFilePath))
            {
                string[] lines = File.ReadAllLines(envFilePath);
                bool tokenFound = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("PLEX_TOKEN="))
                    {
                        lines[i] = $"PLEX_TOKEN={token}";
                        tokenFound = true;
                        break;
                    }
                }

                if (!tokenFound)
                {
                    // Token doesn't exist in the file, add it
                    Array.Resize(ref lines, lines.Length + 1);
                    lines[lines.Length - 1] = $"PLEX_TOKEN={token}";
                }

                File.WriteAllLines(envFilePath, lines);
                Logs.Debug("Updated PLEX_TOKEN in .env file");
            }
            else
            {
                // .env file doesn't exist, create it
                File.WriteAllText(envFilePath, $"PLEX_TOKEN={token}\n");
                Logs.Debug("Created .env file with PLEX_TOKEN");
            }
        }
        catch (Exception ex)
        {
            throw new AuthenticationException($"Failed to save token to .env file: {ex.Message}", "TokenStorage", ex);
        }
    }
}
