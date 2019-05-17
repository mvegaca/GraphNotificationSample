using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Security.Authentication.Web;
using Xamarin.Auth;

namespace GraphNotificationSample.Helpers
{
    public class MSAOAuthHelpers
    {
        static readonly string ProdAuthorizeUrl = "https://login.live.com/oauth20_authorize.srf";
        static readonly string ProdRedirectUrl = "https://login.microsoftonline.com/common/oauth2/nativeclient";
        static readonly string ProdAccessTokenUrl = "https://login.live.com/oauth20_token.srf";

        static readonly string OfflineAccessScope = "wl.offline_access";
        static readonly string WNSScope = "wns.connect";
        static readonly string DdsScope = "dds.register dds.read";
        static readonly string CCSScope = "ccs.ReadWrite";
        static readonly string UserActivitiesScope = "https://activity.windows.com/UserActivity.ReadWrite.CreatedByApp";
        static readonly string UserNotificationsScope = "https://activity.windows.com/Notifications.ReadWrite.CreatedByApp";

        static Random Randomizer = new Random((int)DateTime.Now.Ticks);
        static SHA256 HashProvider = SHA256.Create();

        private static string _msaClientId = ConfigurationManager.AppSettings["MsaClientId"];

        static async Task<IDictionary<string, string>> RequestAccessTokenAsync(string accessTokenUrl, IDictionary<string, string> queryValues)
        {
            // mc++ changed protected to public for extension methods RefreshToken (Adrian Stevens) 
            var content = new FormUrlEncodedContent(queryValues);

            HttpClient client = new HttpClient();
            HttpResponseMessage response = await client.PostAsync(accessTokenUrl, content).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // Parse the response
            IDictionary<string, string> data = text.Contains("{") ? WebEx.JsonDecode(text) : WebEx.FormDecode(text);
            if (data.ContainsKey("error"))
            {
                throw new AuthException(data["error_description"]);
            }

            return data;
        }

        public static async Task<string> GetRefreshTokenAsync()
        {
            byte[] buffer = new byte[32];
            Randomizer.NextBytes(buffer);
            var codeVerifier = Convert.ToBase64String(buffer).Replace('+', '-').Replace('/', '_').Replace("=", "");

            byte[] hash = HashProvider.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            var codeChallenge = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').Replace("=", "");

            var redirectUri = new Uri(ProdRedirectUrl);

            string scope = $"{OfflineAccessScope} {WNSScope} {CCSScope} {UserNotificationsScope} {UserActivitiesScope} {DdsScope}";
            var startUri = new Uri($"{ProdAuthorizeUrl}?client_id={_msaClientId}&response_type=code&code_challenge_method=S256&code_challenge={codeChallenge}&redirect_uri={ProdRedirectUrl}&scope={scope}");

            var webAuthenticationResult = await WebAuthenticationBroker.AuthenticateAsync(
                WebAuthenticationOptions.None,
                startUri,
                redirectUri);

            if (webAuthenticationResult.ResponseStatus == WebAuthenticationStatus.Success)
            {
                var codeResponseUri = new Uri(webAuthenticationResult.ResponseData);
                IDictionary<string, string> queryParams = WebEx.FormDecode(codeResponseUri.Query);
                if (!queryParams.ContainsKey("code"))
                {
                    return string.Empty;
                }

                string authCode = queryParams["code"];
                Dictionary<string, string> refreshTokenQuery = new Dictionary<string, string>
                {
                    { "client_id", _msaClientId },
                    { "redirect_uri", redirectUri.AbsoluteUri },
                    { "grant_type", "authorization_code" },
                    { "code", authCode },
                    { "code_verifier", codeVerifier },
                    { "scope", WNSScope }
                };

                IDictionary<string, string> refreshTokenResponse = await RequestAccessTokenAsync(ProdAccessTokenUrl, refreshTokenQuery);
                if (refreshTokenResponse.ContainsKey("refresh_token"))
                {
                    return refreshTokenResponse["refresh_token"];
                }
            }

            return string.Empty;
        }

        public static async Task<string> GetAccessTokenUsingRefreshTokenAsync(string refreshToken, IReadOnlyList<string> scopes)
        {
            Dictionary<string, string> accessTokenQuery = new Dictionary<string, string>
            {
                { "client_id", _msaClientId },
                { "redirect_uri", ProdRedirectUrl },
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "scope", string.Join(" ", scopes.ToArray()) },
            };

            IDictionary<string, string> accessTokenResponse = await RequestAccessTokenAsync(ProdAccessTokenUrl, accessTokenQuery);
            if (accessTokenResponse == null || !accessTokenResponse.ContainsKey("access_token"))
            {
                throw new Exception("Unable to fetch access_token!");
            }

            return accessTokenResponse["access_token"];
        }
    }
}
