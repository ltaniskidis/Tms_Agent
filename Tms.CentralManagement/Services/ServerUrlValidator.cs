using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Tms.CentralManagement.Services
{
    public class UrlValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? NormalizedUrl { get; set; }
        public long ResponseTimeMs { get; set; }
        public string? ServerVersion { get; set; }
    }

    public interface IServerUrlValidator
    {
        Task<UrlValidationResult> ValidateAsync(string? rawUrl, bool allowEmpty = true);
    }

    public class ServerUrlValidator : IServerUrlValidator
    {
        private readonly ILogger<ServerUrlValidator> _logger;
        private static readonly HttpClientHandler _insecureHandler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };
        private static readonly HttpClient _httpClient = new HttpClient(_insecureHandler)
        {
            Timeout = TimeSpan.FromSeconds(6)
        };

        public ServerUrlValidator(ILogger<ServerUrlValidator> logger)
        {
            _logger = logger;
        }

        public async Task<UrlValidationResult> ValidateAsync(string? rawUrl, bool allowEmpty = true)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                if (allowEmpty)
                {
                    return new UrlValidationResult
                    {
                        IsValid = true,
                        NormalizedUrl = string.Empty
                    };
                }

                return new UrlValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Το URL δεν μπορεί να είναι κενό."
                };
            }

            var trimmed = rawUrl.Trim();

            // Check scheme
            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return new UrlValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Το URL πρέπει να ξεκινάει με http:// ή https:// (π.χ. https://tmsagent.cdgr.dev)."
                };
            }

            // Check absolute URI
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrWhiteSpace(uri.Host))
            {
                return new UrlValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Η μορφή του URL δεν είναι έγκυρη."
                };
            }

            // Normalize URL (strip trailing slashes, preserve custom port and root path if any)
            var normalized = $"{uri.Scheme}://{uri.Authority}".TrimEnd('/');
            if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
            {
                normalized += uri.AbsolutePath.TrimEnd('/');
            }

            var sw = Stopwatch.StartNew();

            try
            {
                // 1. Try dedicated ping endpoint: GET /api/updates/ping
                try
                {
                    using var pingRequest = new HttpRequestMessage(HttpMethod.Get, $"{normalized}/api/updates/ping");
                    using var pingResponse = await _httpClient.SendAsync(pingRequest);
                    
                    if (pingResponse.IsSuccessStatusCode)
                    {
                        var content = await pingResponse.Content.ReadAsStringAsync();
                        string? version = null;
                        try
                        {
                            using var doc = JsonDocument.Parse(content);
                            if (doc.RootElement.TryGetProperty("version", out var vProp))
                            {
                                version = vProp.GetString();
                            }
                        }
                        catch { }

                        sw.Stop();
                        return new UrlValidationResult
                        {
                            IsValid = true,
                            NormalizedUrl = normalized,
                            ResponseTimeMs = sw.ElapsedMilliseconds,
                            ServerVersion = version
                        };
                    }
                }
                catch (Exception ex) when (!(ex is TaskCanceledException) && !(ex is HttpRequestException))
                {
                    _logger.LogDebug(ex, "Ping test failed on {Url}", normalized);
                }

                // 2. Fallback: POST /api/updates/check with empty payload
                // Any TMS Central server will return 401 Unauthorized with "API Key is required."
                try
                {
                    using var checkRequest = new HttpRequestMessage(HttpMethod.Post, $"{normalized}/api/updates/check")
                    {
                        Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
                    };
                    using var checkResponse = await _httpClient.SendAsync(checkRequest);
                    
                    if (checkResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        var content = await checkResponse.Content.ReadAsStringAsync();
                        if (content.Contains("API Key", StringComparison.OrdinalIgnoreCase))
                        {
                            sw.Stop();
                            return new UrlValidationResult
                            {
                                IsValid = true,
                                NormalizedUrl = normalized,
                                ResponseTimeMs = sw.ElapsedMilliseconds
                            };
                        }
                    }
                }
                catch (Exception ex) when (!(ex is TaskCanceledException) && !(ex is HttpRequestException))
                {
                    _logger.LogDebug(ex, "Updates check test failed on {Url}", normalized);
                }

                // 3. Fallback: GET /Login
                // TMS Central web interface responds to /Login with 200 OK
                try
                {
                    using var loginRequest = new HttpRequestMessage(HttpMethod.Get, $"{normalized}/Login");
                    using var loginResponse = await _httpClient.SendAsync(loginRequest);

                    if (loginResponse.IsSuccessStatusCode)
                    {
                        var content = await loginResponse.Content.ReadAsStringAsync();
                        if (content.Contains("TMS Central", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("Central Management", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("Κεντρική Διαχείριση", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains("Σύνδεση", StringComparison.OrdinalIgnoreCase))
                        {
                            sw.Stop();
                            return new UrlValidationResult
                            {
                                IsValid = true,
                                NormalizedUrl = normalized,
                                ResponseTimeMs = sw.ElapsedMilliseconds
                            };
                        }
                    }
                }
                catch (Exception ex) when (!(ex is TaskCanceledException) && !(ex is HttpRequestException))
                {
                    _logger.LogDebug(ex, "Login test failed on {Url}", normalized);
                }

                sw.Stop();
                return new UrlValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Ο διακομιστής ανταποκρίνεται, αλλά δεν φαίνεται να εκτελεί την υπηρεσία TMS Central (δεν εντοπίστηκε το TMS Central API ή η σελίδα εισόδου)."
                };
            }
            catch (TaskCanceledException)
            {
                sw.Stop();
                return new UrlValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Το χρονικό όριο έληξε χωρίς απάντηση (Timeout 6s). Ο διακομιστής δεν ανταποκρίνεται ή η θύρα είναι κλειστή από Firewall."
                };
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                var msg = ex.Message;
                if (ex.InnerException is SocketException sockEx)
                {
                    if (sockEx.SocketErrorCode == SocketError.HostNotFound || 
                        sockEx.SocketErrorCode == SocketError.NoData)
                    {
                        return new UrlValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = "Αδυναμία εύρεσης διακομιστή (DNS Host not found). Ελέγξτε την ορθογραφία του domain."
                        };
                    }
                    if (sockEx.SocketErrorCode == SocketError.ConnectionRefused)
                    {
                        return new UrlValidationResult
                        {
                            IsValid = false,
                            ErrorMessage = $"Η σύνδεση απορρίφθηκε από τον διακομιστή (Connection refused στη θύρα {uri.Port}). Βεβαιωθείτε ότι η εφαρμογή εκτελείται."
                        };
                    }
                    msg = sockEx.Message;
                }
                else if (msg.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase) ||
                         msg.Contains("No such host is known", StringComparison.OrdinalIgnoreCase))
                {
                    return new UrlValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Αδυναμία εύρεσης διακομιστή (DNS Host not found). Ελέγξτε την ορθογραφία του domain."
                    };
                }

                return new UrlValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Σφάλμα σύνδεσης: {msg}"
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new UrlValidationResult
                {
                    IsValid = false,
                    ErrorMessage = $"Σφάλμα ελέγχου διακομιστή: {ex.Message}"
                };
            }
        }
    }
}
