﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MidaxLib;

namespace MidaxTester
{
    public class DailyReplay
    {
        public static void Run(List<DateTime> dates, bool generate = false, bool publish_to_db = false)
        {
            Config.Settings = new Dictionary<string, string>();
            Config.Settings["TRADING_MODE"] = "REPLAY";
            Config.Settings["REPLAY_MODE"] = "CSV";
            Config.Settings["TRADING_LIMIT_PER_BP"] = "10";
            Config.Settings["DB_CONTACTPOINT"] = "192.168.1.26";
            Config.Settings["TIMESERIES_MAX_RECORD_TIME_HOURS"] = "12";
            List<MarketData> stocks = new List<MarketData>();
            List<MarketData> volIndices = new List<MarketData>();
            //volIndices.Add(new MarketData("IN.D.VIX.MONTH2.IP"));
            //volIndices.Add(new MarketData("IN.D.VIX.MONTH3.IP"));
            foreach (var test in dates)
            {
                List<string> mktdataFiles = new List<string>();
                mktdataFiles.Add(string.Format("..\\..\\expected_results\\mktdata_{0}_{1}_{2}.csv", test.Day, test.Month, test.Year));
                Config.Settings["REPLAY_CSV"] = Config.TestList(mktdataFiles);
                Config.Settings["PUBLISHING_START_TIME"] = string.Format("{0}-{1}-{2} {3}:{4}:{5}", test.Year, test.Month, test.Day, 6, 45, 0);
                Config.Settings["PUBLISHING_STOP_TIME"] = string.Format("{0}-{1}-{2} {3}:{4}:{5}", test.Year, test.Month, test.Day, 18, 0, 0);
                Config.Settings["TRADING_START_TIME"] = string.Format("{0}-{1}-{2} {3}:{4}:{5}", test.Year, test.Month, test.Day, 8, 0, 0);
                Config.Settings["TRADING_STOP_TIME"] = string.Format("{0}-{1}-{2} {3}:{4}:{5}", test.Year, test.Month, test.Day, 17, 0, 0);
                Config.Settings["TRADING_CLOSING_TIME"] = string.Format("{0}-{1}-{2} {3}:{4}:{5}", test.Year, test.Month, test.Day, 16, 55, 0);
                if (generate)
                {
                    if (publish_to_db)
                        Config.Settings["PUBLISHING_DB"] = Config.Settings["DB_CONTACTPOINT"];
                    else
                        Config.Settings["PUBLISHING_CSV"] = string.Format("..\\..\\expected_results\\mktdatagen_{0}_{1}_{2}.csv", test.Day, test.Month, test.Year);
                }

                MarketDataConnection.Instance.Connect(null);
                MarketData index = new MarketData("DAX:IX.D.DAX.DAILY.IP");
                ModelTest model = new ModelTest(index, stocks, volIndices);
                Console.WriteLine("Running a new daily record...");
                model.StartSignals();
                model.StopSignals();
            }
        }
    }
}