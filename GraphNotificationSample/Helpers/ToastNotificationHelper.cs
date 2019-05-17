using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace GraphNotificationSample.Helpers
{
    public static class ToastNotificationHelper
    {
        public static ToastNotification BuildToastNotification(string notificationId, string notificationContent)
        {
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            XmlNodeList toastNodeList = toastXml.GetElementsByTagName("text");
            toastNodeList.Item(0).AppendChild(toastXml.CreateTextNode(notificationId));
            toastNodeList.Item(1).AppendChild(toastXml.CreateTextNode(notificationContent));
            IXmlNode toastNode = toastXml.SelectSingleNode("/toast");
            ((XmlElement)toastNode).SetAttribute("launch", "{\"type\":\"toast\",\"notificationId\":\"" + notificationId + "\"}");
            XmlElement audio = toastXml.CreateElement("audio");
            audio.SetAttribute("src", "ms-winsoundevent:Notification.SMS");
            return new ToastNotification(toastXml)
            {
                Tag = notificationId
            };
        }
    }
}
