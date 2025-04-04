using PlexBot.Core.Exceptions;
using PlexBot.Utils;

namespace PlexBot.Utils.Http;

/// <summary>
/// Wraps HttpClient to provide consistent error handling, logging, and retry behavior.
/// This class centralizes HTTP communication logic, ensuring that all API calls follow
/// the same patterns for authorization, error handling, and response processing while
/// providing detailed logging for diagnostics.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="HttpClientWrapper"/> class.
/// Creates a wrapper around an existing HttpClient with custom service name and retry settings.
/// </remarks>
/// <param name="httpClient">The HttpClient to wrap</param>
/// <param name="serviceName">A descriptive name for the service being called (for logging)</param>
/// <param name="maxRetries">Maximum number of retry attempts for failed requests</param>
/// <param name="retryDelaySec">Base delay in seconds between retry attempts (will be exponentially increased)</param>
public class HttpClientWrapper(HttpClient httpClient, string serviceName, int maxRetries = 3, int retryDelaySec = 1)
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly string _serviceName = serviceName ?? throw new ArgumentNullException(nameof(serviceName));
    private readonly int _maxRetries = maxRetries;
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(retryDelaySec);

    /// <summary>
    /// Sends a GET request to the specified URI with retry logic.
    /// Handles the complete request lifecycle including retries on transient errors
    /// and consistent error handling for different failure scenarios.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to</typeparam>
    /// <param name="uri">The URI to send the request to</param>
    /// <param name="headers">Optional headers to include with the request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The deserialized response object</returns>
    /// <exception cref="PlexApiException">Thrown when the request fails after all retry attempts</exception>
    public async Task<T> GetAsync<T>(
        string uri,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<T>(HttpMethod.Get, uri, null, headers, cancellationToken);
    }

    /// <summary>
    /// Sends a POST request to the specified URI with retry logic.
    /// Handles the complete request lifecycle including JSON serialization of the content,
    /// retries on transient errors, and consistent error handling for different failure scenarios.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to</typeparam>
    /// <param name="uri">The URI to send the request to</param>
    /// <param name="content">The content to send with the request (will be JSON serialized)</param>
    /// <param name="headers">Optional headers to include with the request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The deserialized response object</returns>
    /// <exception cref="PlexApiException">Thrown when the request fails after all retry attempts</exception>
    public async Task<T> PostAsync<T>(
        string uri,
        object? content = null,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<T>(HttpMethod.Post, uri, content, headers, cancellationToken);
    }

    /// <summary>
    /// Sends a PUT request to the specified URI with retry logic.
    /// Handles the complete request lifecycle including JSON serialization of the content,
    /// retries on transient errors, and consistent error handling for different failure scenarios.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to</typeparam>
    /// <param name="uri">The URI to send the request to</param>
    /// <param name="content">The content to send with the request (will be JSON serialized)</param>
    /// <param name="headers">Optional headers to include with the request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The deserialized response object</returns>
    /// <exception cref="PlexApiException">Thrown when the request fails after all retry attempts</exception>
    public async Task<T> PutAsync<T>(
        string uri,
        object? content = null,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        return await SendRequestAsync<T>(HttpMethod.Put, uri, content, headers, cancellationToken);
    }

    /// <summary>
    /// Sends a request with the specified method, URI, and content with retry logic.
    /// This is the core method that implements the retry behavior, error handling, and
    /// JSON processing common to all HTTP methods.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to</typeparam>
    /// <param name="method">The HTTP method to use</param>
    /// <param name="uri">The URI to send the request to</param>
    /// <param name="content">The content to send (will be JSON serialized if not null)</param>
    /// <param name="headers">Optional headers to include with the request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The deserialized response object</returns>
    /// <exception cref="PlexApiException">Thrown when the request fails after all retry attempts</exception>
    private async Task<T> SendRequestAsync<T>(
        HttpMethod method,
        string uri,
        object? content = null,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        int attemptCount = 0;
        Exception? lastException = null;

        // Implement retry logic
        while (attemptCount <= _maxRetries)
        {
            try
            {
                attemptCount++;
                Logs.Debug($"[{_serviceName}] {method} request to {uri} (Attempt {attemptCount}/{_maxRetries + 1})");

                using var request = new HttpRequestMessage(method, uri);

                // Add headers
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                // Add content if provided
                if (content != null)
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(content);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                // Send request
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                // Get response content even for error status codes
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Handle unsuccessful status codes
                if (!response.IsSuccessStatusCode)
                {
                    Logs.Warning($"[{_serviceName}] Request failed with status {response.StatusCode}: {responseBody}");

                    // Determine if we should retry based on status code
                    if (ShouldRetry(response.StatusCode) && attemptCount <= _maxRetries)
                    {
                        TimeSpan delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1));
                        Logs.Debug($"[{_serviceName}] Retrying after {delay.TotalSeconds:N1} seconds...");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    throw new PlexApiException(
                        $"Request to {_serviceName} failed with status code {response.StatusCode}",
                        response.StatusCode,
                        uri,
                        responseBody);
                }

                // For success responses, try to deserialize
                try
                {
                    T result = System.Text.Json.JsonSerializer.Deserialize<T>(responseBody, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? throw new InvalidOperationException("Deserialization returned null");

                    Logs.Debug($"[{_serviceName}] Request successful");
                    return result;
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw new PlexApiException(
                        $"Failed to deserialize response from {_serviceName}: {ex.Message}",
                        ex);
                }
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                Logs.Warning($"[{_serviceName}] HTTP error: {ex.Message}");

                // For connection-level errors, we should retry
                if (attemptCount <= _maxRetries)
                {
                    TimeSpan delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1));
                    Logs.Debug($"[{_serviceName}] Retrying after {delay.TotalSeconds:N1} seconds...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // This is a timeout rather than a cancellation request
                lastException = ex;
                Logs.Warning($"[{_serviceName}] Request timed out: {ex.Message}");

                if (attemptCount <= _maxRetries)
                {
                    TimeSpan delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1));
                    Logs.Debug($"[{_serviceName}] Retrying after {delay.TotalSeconds:N1} seconds...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                // This is an explicit cancellation request, don't retry
                Logs.Info($"[{_serviceName}] Request was canceled");
                throw;
            }
            catch (PlexApiException)
            {
                // Already formatted exception, just rethrow
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logs.Error($"[{_serviceName}] Unexpected error: {ex.Message}");

                // For unexpected errors, we should not retry
                break;
            }
        }

        // If we get here, all retries failed or an unretryable error occurred
        throw new PlexApiException(
            $"Request to {_serviceName} failed after {attemptCount} attempts",
            lastException ?? new InvalidOperationException("Unknown error"));
    }

    /// <summary>
    /// Sends a request and returns the raw string response.
    /// Useful for cases where the response isn't JSON or when the raw content is needed.
    /// </summary>
    /// <param name="method">The HTTP method to use</param>
    /// <param name="uri">The URI to send the request to</param>
    /// <param name="content">The content to send (will be JSON serialized if not null)</param>
    /// <param name="headers">Optional headers to include with the request</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The raw response string</returns>
    /// <exception cref="PlexApiException">Thrown when the request fails after all retry attempts</exception>
    public async Task<string> SendRequestForStringAsync(
        HttpMethod method,
        string uri,
        object? content = null,
        Dictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        int attemptCount = 0;
        Exception? lastException = null;

        // Implement retry logic
        while (attemptCount <= _maxRetries)
        {
            try
            {
                attemptCount++;
                Logs.Debug($"[{_serviceName}] {method} request to {uri} (Attempt {attemptCount}/{_maxRetries + 1})");

                using var request = new HttpRequestMessage(method, uri);

                // Add headers
                if (headers != null)
                {
                    foreach (var header in headers)
                    {
                        request.Headers.Add(header.Key, header.Value);
                    }
                }

                // Add content if provided
                if (content != null)
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(content);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                // Send request
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                // Get response content even for error status codes
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Handle unsuccessful status codes
                if (!response.IsSuccessStatusCode)
                {
                    Logs.Warning($"[{_serviceName}] Request failed with status {response.StatusCode}: {responseBody}");

                    // Determine if we should retry based on status code
                    if (ShouldRetry(response.StatusCode) && attemptCount <= _maxRetries)
                    {
                        TimeSpan delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1));
                        Logs.Debug($"[{_serviceName}] Retrying after {delay.TotalSeconds:N1} seconds...");
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    throw new PlexApiException(
                        $"Request to {_serviceName} failed with status code {response.StatusCode}",
                        response.StatusCode,
                        uri,
                        responseBody);
                }

                Logs.Debug($"[{_serviceName}] Request successful");
                return responseBody;
            }
            catch (HttpRequestException ex)
            {
                lastException = ex;
                Logs.Warning($"[{_serviceName}] HTTP error: {ex.Message}");

                // For connection-level errors, we should retry
                if (attemptCount <= _maxRetries)
                {
                    TimeSpan delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1));
                    Logs.Debug($"[{_serviceName}] Retrying after {delay.TotalSeconds:N1} seconds...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // This is a timeout rather than a cancellation request
                lastException = ex;
                Logs.Warning($"[{_serviceName}] Request timed out: {ex.Message}");

                if (attemptCount <= _maxRetries)
                {
                    TimeSpan delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1));
                    Logs.Debug($"[{_serviceName}] Retrying after {delay.TotalSeconds:N1} seconds...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                // This is an explicit cancellation request, don't retry
                Logs.Info($"[{_serviceName}] Request was canceled");
                throw;
            }
            catch (PlexApiException)
            {
                // Already formatted exception, just rethrow
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                Logs.Error($"[{_serviceName}] Unexpected error: {ex.Message}");

                // For unexpected errors, we should not retry
                break;
            }
        }

        // If we get here, all retries failed or an unretryable error occurred
        throw new PlexApiException(
            $"Request to {_serviceName} failed after {attemptCount} attempts",
            lastException ?? new InvalidOperationException("Unknown error"));
    }

    /// <summary>
    /// Determines whether a request should be retried based on its status code.
    /// Only certain status codes that indicate transient errors should trigger retries,
    /// while others (like authentication errors) should fail immediately.
    /// </summary>
    /// <param name="statusCode">The HTTP status code of the response</param>
    /// <returns>True if the request should be retried; otherwise, false</returns>
    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        // Retry server errors and certain client errors
        return statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == HttpStatusCode.TooManyRequests ||
               statusCode == HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout;
    }
}