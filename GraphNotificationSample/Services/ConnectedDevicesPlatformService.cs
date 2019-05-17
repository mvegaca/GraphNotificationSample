using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using GraphNotificationSample.Activation;
using GraphNotificationSample.Helpers;
using Microsoft.ConnectedDevices;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Windows.Storage;

namespace GraphNotificationSample.Services
{
    public class ConnectedDevicesPlatformService
    {
        private const string AccountsKey = "Accounts";

        private ConnectedDevicesPlatform _platform;
        private List<Account> _accounts = new List<Account>();

        public event EventHandler AccountsChanged;

        public ConnectedDevicesPlatformService()
        {
        }

        public async Task InitializeAsync()
        {
            _platform = new ConnectedDevicesPlatform();
            _platform.AccountManager.AccessTokenRequested += AccessTokenRequestedAsync;
            _platform.AccountManager.AccessTokenInvalidated += AccessTokenInvalidated;
            _platform.NotificationRegistrationManager.NotificationRegistrationStateChanged += NotificationRegistrationStateChanged;
            _platform.Start();

            await DeserializeAccountsAsync();
            await InitializeAccountsAsync();
        }

        private async Task DeserializeAccountsAsync()
        {
            var sdkCachedAccounts = _platform.AccountManager.Accounts.ToList();
            var appCachedAccounts = await ApplicationData.Current.LocalSettings.ReadAsync<string>(AccountsKey);
            if (!string.IsNullOrEmpty(appCachedAccounts))
            {
                DeserializeAppCachedAccounts(appCachedAccounts, sdkCachedAccounts);
            }

            // Add the remaining SDK only accounts (these need to be removed from the SDK)
            foreach (var sdkCachedAccount in sdkCachedAccounts)
            {
                _accounts.Add(new Account(_platform, sdkCachedAccount.Id, sdkCachedAccount.Type, null, AccountRegistrationState.InSdkCacheOnly));
            }
        }        

        private void DeserializeAppCachedAccounts(string jsonCachedAccounts, List<ConnectedDevicesAccount> sdkCachedAccounts)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonCachedAccounts));
            var serializer = new DataContractJsonSerializer(_accounts.GetType());
            var appCachedAccounts = serializer.ReadObject(stream) as List<Account>;

            var authContext = new AuthenticationContext("https://login.microsoftonline.com/common");
            var adalCachedItems = authContext.TokenCache.ReadItems();
            foreach (var account in appCachedAccounts)
            {
                if (account.Type == ConnectedDevicesAccountType.AAD)
                {
                    // AAD accounts are also cached in ADAL, which is where the actual token logic lives.
                    // If the account isn't available in our ADAL cache then it's not usable. Ideally this
                    // shouldn't happen.
                    var adalCachedItem = adalCachedItems.FirstOrDefault((x) => x.UniqueId == account.Id);
                    if (adalCachedItem == null)
                    {
                        continue;
                    }
                }

                // Check if the account is also present in ConnectedDevicesPlatform.AccountManager.
                AccountRegistrationState registrationState;
                var sdkAccount = sdkCachedAccounts.Find((x) => account.EqualsTo(x));
                if (sdkAccount == null)
                {
                    // Account not found in the SDK cache. Later when Account.InitializeAsync runs this will 
                    // add the account to the SDK cache and perform registration.
                    registrationState = AccountRegistrationState.InAppCacheOnly;
                }
                else
                {
                    // Account found in the SDK cache, remove it from the list of sdkCachedAccounts. After 
                    // all the appCachedAccounts have been processed any accounts remaining in sdkCachedAccounts
                    // are only in the SDK cache, and should be removed.
                    registrationState = AccountRegistrationState.InAppCacheAndSdkCache;
                    sdkCachedAccounts.RemoveAll((x) => account.EqualsTo(x));
                }

                _accounts.Add(new Account(_platform, account.Id, account.Type, account.Token, registrationState));
            }
        }

        private async Task InitializeAccountsAsync()
        {
            foreach (var account in _accounts)
            {
                await account.InitializeAccountAsync();
            }

            // All accounts which can be in a good state should be. Remove any accounts which aren't
            _accounts.RemoveAll((x) => x.RegistrationState != AccountRegistrationState.InAppCacheAndSdkCache);
            AccountListChanged();
        }

        private void AccountListChanged()
        {
            AccountsChanged.Invoke(this, new EventArgs());
            SerializeAccountsToCache();
        }

        private void SerializeAccountsToCache()
        {
            using (var stream = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(_accounts.GetType());
                serializer.WriteObject(stream, _accounts);
                var json = stream.ToArray();
                ApplicationData.Current.LocalSettings.Values[AccountsKey] = Encoding.UTF8.GetString(json, 0, json.Length);
            }
        }

        private void AccessTokenInvalidated(ConnectedDevicesAccountManager sender, ConnectedDevicesAccessTokenInvalidatedEventArgs args)
        {
            Logger.LogMessage($"Token Invalidated. AccountId: {args.Account.Id}, AccountType: {args.Account.Id}, scopes: {string.Join(" ", args.Scopes)}");
        }        

        private async void NotificationRegistrationStateChanged(ConnectedDevicesNotificationRegistrationManager sender, ConnectedDevicesNotificationRegistrationStateChangedEventArgs args)
        {
            if ((args.State == ConnectedDevicesNotificationRegistrationState.Expired) || (args.State == ConnectedDevicesNotificationRegistrationState.Expiring))
            {
                var account = _accounts.Find((x) => x.EqualsTo(args.Account));
                if (account != null)
                {
                    await account.RegisterAccountWithSdkAsync();
                }
            }
        }

        private async void AccessTokenRequestedAsync(ConnectedDevicesAccountManager sender, ConnectedDevicesAccessTokenRequestedEventArgs args)
        {
            Logger.LogMessage($"Token requested by platform for {args.Request.Account.Id} and {string.Join(" ", args.Request.Scopes)}");

            var account = _accounts.Find((x) => x.EqualsTo(args.Request.Account));
            if (account != null)
            {
                try
                {
                    var accessToken = await account.GetAccessTokenAsync(args.Request.Scopes);
                    Logger.LogMessage($"Token : {accessToken}");
                    args.Request.CompleteWithAccessToken(accessToken);
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Token request failed: {ex.Message}");
                    args.Request.CompleteWithErrorMessage(ex.Message);
                }
            }
        }
    }
}
