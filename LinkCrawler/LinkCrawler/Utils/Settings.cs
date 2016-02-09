﻿using System.Configuration;
using LinkCrawler.Utils.Extensions;

namespace LinkCrawler.Utils
{
    public class Settings
    {
        private static Settings _instance;
        public static Settings Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;
                return _instance = new Settings();
            }
        }

        public string BaseUrl => 
            ConfigurationManager.AppSettings[Constants.AppSettings.BaseUrl];

        public bool CheckImages => 
            ConfigurationManager.AppSettings[Constants.AppSettings.CheckImages].ToBool();

        public string SlackWebHookUrl => 
            ConfigurationManager.AppSettings[Constants.AppSettings.SlackWebHookUrl];

        public string SlackWebHookBotName => 
            ConfigurationManager.AppSettings[Constants.AppSettings.SlackWebHookBotName];

        public string SlackWebHookBotIconEmoji => 
            ConfigurationManager.AppSettings[Constants.AppSettings.SlackWebHookBotIconEmoji];
    }
}