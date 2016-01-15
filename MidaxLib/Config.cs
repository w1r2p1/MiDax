﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MidaxLib
{
    public class Config
    {
        static protected Dictionary<string, string> _settings = null;
        
        public static Dictionary<string, string> Settings
        {
            get { return _settings; }
            set { _settings = value; }
        }

        public static bool TradingEnabled
        {
            get
            {
                return _settings["TRADING_MODE"] == "PRODUCTION";
            }
        }

        public static bool ReplayEnabled
        {
            get
            {
                return _settings["TRADING_MODE"] == "REPLAY";
            }
        }

        public static bool CalibratorEnabled
        {
            get
            {
                return _settings["TRADING_MODE"] == "CALIBRATION";
            }
        }

        public static bool MarketSelectorEnabled
        {
            get
            {
                return _settings["TRADING_MODE"] == "SELECT";
            }
        }

        public static bool TestReplayEnabled
        {
            get
            {
                if (!Config.Settings.ContainsKey("REPLAY_MODE"))
                    return false;
                return ReplayEnabled && (!Config.Settings.ContainsKey("PUBLISHING_CSV") && !Config.Settings.ContainsKey("DB_CONTACTPOINT"));
            }
        }

        public static bool TestReplayGeneratorEnabled
        {
            get
            {
                return ReplayEnabled && Config.Settings.ContainsKey("PUBLISHING_CSV");
            }
        }

        public static bool UATSourceDB
        {
            get
            {
                return Config.Settings["TRADING_MODE"] == "UAT";
            }
        }
                
        public static bool TradingOpen(DateTime time)
        {
            return time.TimeOfDay > Config.ParseDateTimeLocal(_settings["TRADING_START_TIME"]).TimeOfDay &&
                    time.TimeOfDay < Config.ParseDateTimeLocal(_settings["TRADING_STOP_TIME"]).TimeOfDay;
        }

        public static bool PublishingOpen(DateTime time)
        {
            if (ReplayEnabled || MarketSelectorEnabled)
                return true;
            return time.TimeOfDay > Config.ParseDateTimeLocal(_settings["PUBLISHING_START_TIME"]).TimeOfDay &&
                time.TimeOfDay < Config.ParseDateTimeLocal(_settings["PUBLISHING_STOP_TIME"]).TimeOfDay;
        }        

        public static string TestList(List<string> tests)
        {
            return tests.Aggregate("", (prev, next) => prev + next + ";", res => res.Substring(0, res.Length - 1));
        }

        public static DateTime ParseDateTimeUTC(string dt)
        {
            return DateTime.SpecifyKind(DateTime.Parse(dt), DateTimeKind.Utc);
        }

        public static DateTime ParseDateTimeLocal(string dt)
        {
            return DateTime.SpecifyKind(DateTime.Parse(dt), DateTimeKind.Local);
        }
    }
}
