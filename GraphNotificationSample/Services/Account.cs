using System;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using GraphNotificationSample.Helpers;
using Microsoft.ConnectedDevices;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Windows.Networking.PushNotifications;

namespace GraphNotificationSample.Services
{
    [DataContract]
    public class Account
    {
        [DataMember]
        public string Id { get; set; }

        [DataMember]
        public ConnectedDevicesAccountType Type { get; set; }

        [DataMember]
        public string Token { get; set; }

        public AccountRegistrationState RegistrationState { get; set; }

        public UserNotificationsManager UserNotifications { get; set; }

        private ConnectedDevicesPlatform m_platform;

        public Account(ConnectedDevicesPlatform platform, String id,
            ConnectedDevicesAccountType type, String token, AccountRegistrationState registrationState)
        {
            m_platform = platform;
            Id = id;
            Type = type;
            Token = token;
            RegistrationState = registrationState;

            // Accounts can be in 3 different scenarios:
            // 1: cached account in good standing (initialized in the SDK and our token cache).
            // 2: account missing from the SDK but present in our cache: Add and initialize account.
            // 3: account missing from our cache but present in the SDK. Log the account out async

            // Subcomponents (e.g. UserDataFeed) can only be initialized when an account is in both the app cache 
            // and the SDK cache.
            // For scenario 1, immediately initialize our subcomponents.
            // For scenario 2, subcomponents will be initialized after InitializeAccountAsync registers the account with the SDK.
            // For scenario 3, InitializeAccountAsync will unregister the account and subcomponents will never be initialized.
            if (RegistrationState == AccountRegistrationState.InAppCacheAndSdkCache)
            {
                InitializeSubcomponents();
            }
        }

        public bool EqualsTo(ConnectedDevicesAccount other)
        {
            return ((other.Id == Id) && (other.Type == Type));
        }

        public async Task InitializeAccountAsync()
        {
            if (RegistrationState == AccountRegistrationState.InAppCacheOnly)
            {
                // Scenario 2, add the account to the SDK
                var account = new ConnectedDevicesAccount(Id, Type);
                await m_platform.AccountManager.AddAccountAsync(account);
                RegistrationState = AccountRegistrationState.InAppCacheAndSdkCache;

                InitializeSubcomponents();
                await RegisterAccountWithSdkAsync();
            }
            else if (RegistrationState == AccountRegistrationState.InSdkCacheOnly)
            {
                // Scenario 3, remove the account from the SDK
                var account = new ConnectedDevicesAccount(Id, Type);
                await m_platform.AccountManager.RemoveAccountAsync(account);
            }
        }

        public async Task RegisterAccountWithSdkAsync()
        {
            if (RegistrationState != AccountRegistrationState.InAppCacheAndSdkCache)
            {
                throw new Exception("Account must be in both SDK and App cache before it can be registered");
            }

            var channel = await PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync();
            ConnectedDevicesNotificationRegistration registration = new ConnectedDevicesNotificationRegistration();
            registration.Type = ConnectedDevicesNotificationType.WNS;
            registration.Token = channel.Uri;
            var account = new ConnectedDevicesAccount(Id, Type);
            var registerResult = await m_platform.NotificationRegistrationManager.RegisterAsync(account, registration);

            // It would be a good idea for apps to take a look at the different statuses here and perhaps attempt some sort of remediation.
            // For example, web failure may indicate that a web service was temporarily in a bad state and retries may be successful.
            // 
            // NOTE: this approach was chosen rather than using exceptions to help separate "expected" / "retry-able" errors from real 
            // exceptions and keep the error-channel logic clean and simple.
            if (registerResult.Status == ConnectedDevicesNotificationRegistrationStatus.Success)
            {
                await UserNotifications.RegisterAccountWithSdkAsync();
            }
        }

        public async Task LogoutAsync()
        {
            ClearSubcomponents();
            await m_platform.AccountManager.RemoveAccountAsync(new ConnectedDevicesAccount(Id, Type));
            RegistrationState = AccountRegistrationState.InAppCacheOnly;
        }

        private void InitializeSubcomponents()
        {
            if (RegistrationState != AccountRegistrationState.InAppCacheAndSdkCache)
            {
                throw new Exception("Account must be in both SDK and App cache before subcomponents can be initialized");
            }

            var account = new ConnectedDevicesAccount(Id, Type);
            UserNotifications = new UserNotificationsManager(m_platform, account);
        }

        private void ClearSubcomponents()
        {
            UserNotifications.Reset();
            UserNotifications = null;
        }

        public async Task<string> GetAccessTokenAsync(IReadOnlyList<string> scopes)
        {
            if (Type == ConnectedDevicesAccountType.MSA)
            {
                return await MSAOAuthHelpers.GetAccessTokenUsingRefreshTokenAsync(Token, scopes);
            }
            else if (Type == ConnectedDevicesAccountType.AAD)
            {
                var authContext = new AuthenticationContext("https://login.microsoftonline.com/common");

                UserIdentifier aadUserId = new UserIdentifier(Id, UserIdentifierType.UniqueId);
                AuthenticationResult result;
                var aadClientId = ConfigurationManager.AppSettings["AadClientId"];
                try
                {
                    result = await authContext.AcquireTokenSilentAsync(scopes[0], aadClientId);
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Token request failed: {ex.Message}");

                    // Token may have expired, try again non-silently
                    var aadRedirectUri = ConfigurationManager.AppSettings["AadRedirectUri"];
                    result = await authContext.AcquireTokenAsync(scopes[0], aadClientId,
                        new Uri(aadRedirectUri), new PlatformParameters(PromptBehavior.Auto, true));
                }

                return result.AccessToken;
            }
            else
            {
                throw new Exception("Invalid Account Type");
            }
        }

        public static async Task<AuthenticationResult> GetAadTokenAsync(string scope)
        {
            var aadClientId = ConfigurationManager.AppSettings["AadClientId"];
            var aadRedirectUri = ConfigurationManager.AppSettings["AadRedirectUri"];
            var authContext = new AuthenticationContext("https://login.microsoftonline.com/common");
            return await authContext.AcquireTokenAsync(scope, aadClientId, new Uri(aadRedirectUri),
                new PlatformParameters(PromptBehavior.Auto, true));
        }
    }
}
