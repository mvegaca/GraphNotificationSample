using System;
using System.Collections.Generic;
using System.Configuration;
using System.Threading.Tasks;
using GraphNotificationSample.Helpers;
using Microsoft.ConnectedDevices;
using Microsoft.ConnectedDevices.UserData;
using Microsoft.ConnectedDevices.UserData.UserNotifications;
using WinNotif = Windows.UI.Notifications;


namespace GraphNotificationSample.Services
{
    public class UserNotificationsManager
    {
        private UserDataFeed _feed;
        private UserNotificationReader _reader;
        private UserNotificationChannel _channel;
        private List<UserNotification> _newNotifications = new List<UserNotification>();
        private List<UserNotification> _historicalNotifications = new List<UserNotification>();

        public event EventHandler CacheUpdated;

        public UserNotificationsManager(ConnectedDevicesPlatform platform, ConnectedDevicesAccount account)
        {
            var appHostName = ConfigurationManager.AppSettings["AppHostName"];
            _feed = UserDataFeed.GetForAccount(account, platform, appHostName);
            _feed.SyncStatusChanged += SyncStatusChanged;

            _channel = new UserNotificationChannel(_feed);
            _reader = _channel.CreateReader();
            _reader.DataChanged += DataChanged;
            Logger.LogMessage($"Setup feed for {account.Id} {account.Type}");
        }

        public async Task RegisterAccountWithSdkAsync()
        {
            var scopes = new List<UserDataFeedSyncScope> { UserNotificationChannel.SyncScope };
            bool registered = await _feed.SubscribeToSyncScopesAsync(scopes);
            if (!registered)
            {
                throw new Exception("Subscribe failed");
            }
        }

        private void SyncStatusChanged(UserDataFeed sender, UserDataFeedSyncStatusChangedEventArgs args)
        {
            Logger.LogMessage($"SyncStatus is {sender.SyncStatus.ToString()}");
        }

        private async void DataChanged(UserNotificationReader sender, UserNotificationReaderDataChangedEventArgs args)
        {
            Logger.LogMessage("New notification available");
            await ReadNotificationsAsync(sender);
        }

        public async Task RefreshAsync()
        {
            Logger.LogMessage("Read cached notifications");
            await ReadNotificationsAsync(_reader);

            Logger.LogMessage("Request another sync");
            _feed.StartSync();
        }

        private async Task ReadNotificationsAsync(UserNotificationReader reader)
        {
            var notifications = await reader.ReadBatchAsync(uint.MaxValue);
            Logger.LogMessage($"Read {notifications.Count} notifications");

            foreach (var notification in notifications)
            {
                if (notification.Status == UserNotificationStatus.Active)
                {
                    _newNotifications.RemoveAll((n) => { return (n.Id == notification.Id); });
                    if (notification.UserActionState == UserNotificationUserActionState.NoInteraction)
                    {
                        // Brand new notification, add to new
                        _newNotifications.Add(notification);
                        Logger.LogMessage($"UserNotification not interacted: {notification.Id}");
                        if (!string.IsNullOrEmpty(notification.Content) && notification.ReadState != UserNotificationReadState.Read)
                        {
                            RemoveToastNotification(notification.Id);
                            ShowToastNotification(ToastNotificationHelper.BuildToastNotification(notification.Id, notification.Content));
                        }
                    }
                    else
                    {
                        RemoveToastNotification(notification.Id);
                    }

                    _historicalNotifications.RemoveAll((n) => { return (n.Id == notification.Id); });
                    _historicalNotifications.Insert(0, notification);
                }
                else
                {
                    // Historical notification is marked as deleted, remove from display
                    _newNotifications.RemoveAll((n) => { return (n.Id == notification.Id); });
                    _historicalNotifications.RemoveAll((n) => { return (n.Id == notification.Id); });
                    RemoveToastNotification(notification.Id);
                }
            }

            CacheUpdated?.Invoke(this, new EventArgs());
        }

        public async Task ActivateAsync(string id, bool dismiss)
        {
            var notification = _historicalNotifications.Find((n) => { return (n.Id == id); });
            if (notification != null)
            {
                notification.UserActionState = dismiss ? UserNotificationUserActionState.Dismissed : UserNotificationUserActionState.Activated;
                await notification.SaveAsync();
                RemoveToastNotification(notification.Id);
                Logger.LogMessage($"{notification.Id} is now DISMISSED");
            }
        }

        public async Task MarkReadAsync(string id)
        {
            var notification = _historicalNotifications.Find((n) => { return (n.Id == id); });
            if (notification != null)
            {
                notification.ReadState = UserNotificationReadState.Read;
                await notification.SaveAsync();
                Logger.LogMessage($"{notification.Id} is now READ");
            }
        }

        public async Task DeleteAsync(string id)
        {
            var notification = _historicalNotifications.Find((n) => { return (n.Id == id); });
            if (notification != null)
            {
                await _channel.DeleteUserNotificationAsync(notification.Id);
                Logger.LogMessage($"{notification.Id} is now DELETED");
            }
        }

        private void ShowToastNotification(WinNotif.ToastNotification toast)
        {
            var toastNotifier = WinNotif.ToastNotificationManager.CreateToastNotifier();
            toast.Activated += async (s, e) => await ActivateAsync(s.Tag, false);
            toastNotifier.Show(toast);
        }

        private void RemoveToastNotification(string notificationId)
        {
            WinNotif.ToastNotificationManager.History.Remove(notificationId);
        }        

        public void Reset()
        {
            Logger.LogMessage("Resetting the feed");
            _feed = null;
            _newNotifications.Clear();
            _historicalNotifications.Clear();

            CacheUpdated?.Invoke(this, new EventArgs());
        }               
    }
}
