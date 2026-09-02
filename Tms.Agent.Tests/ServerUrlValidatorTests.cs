using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Tms.CentralManagement.Services;
using Xunit;

namespace Tms.Agent.Tests
{
    public class ServerUrlValidatorTests
    {
        private readonly ServerUrlValidator _validator;

        public ServerUrlValidatorTests()
        {
            _validator = new ServerUrlValidator(NullLogger<ServerUrlValidator>.Instance);
        }

        [Fact]
        public async Task ValidateAsync_EmptyUrl_AllowedByDefault()
        {
            var result = await _validator.ValidateAsync("", allowEmpty: true);
            Assert.True(result.IsValid);
            Assert.Equal(string.Empty, result.NormalizedUrl);
        }

        [Fact]
        public async Task ValidateAsync_EmptyUrl_DisallowedWhenSpecified()
        {
            var result = await _validator.ValidateAsync("", allowEmpty: false);
            Assert.False(result.IsValid);
            Assert.Contains("κενό", result.ErrorMessage);
        }

        [Theory]
        [InlineData("tmsagent.cdgr.dev")]
        [InlineData("ftp://tmsagent.cdgr.dev")]
        [InlineData("not a url at all")]
        public async Task ValidateAsync_InvalidSchemeOrFormat_ReturnsError(string invalidUrl)
        {
            var result = await _validator.ValidateAsync(invalidUrl);
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
        }

        [Fact]
        public async Task ValidateAsync_NonExistentDomain_FailsWithDnsError()
        {
            var result = await _validator.ValidateAsync("https://this-domain-definitely-does-not-exist-tms-test-99999.invalid");
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
            Assert.True(
                result.ErrorMessage.Contains("DNS", System.StringComparison.OrdinalIgnoreCase) || 
                result.ErrorMessage.Contains("διακομιστή", System.StringComparison.OrdinalIgnoreCase) ||
                result.ErrorMessage.Contains("σύνδεσης", System.StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task ValidateAsync_NonTmsServer_FailsTmsServiceCheck()
        {
            // Testing with a site that is up but definitely does not run TMS Central
            var result = await _validator.ValidateAsync("https://example.com");
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("TMS Central", result.ErrorMessage);
        }
    }
}
