﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using dto.endpoint.search;
using IGPublicPcl;
using Lightstreamer.DotNet.Client;

namespace MidaxLib
{
    public abstract class MarketDataConnection
    {
        protected MarketDataSubscription _mktDataListener = null;
        static MarketDataConnection _instance = null;
        protected IAbstractStreamingClient _apiStreamingClient = null;
        protected TimerCallback _callbackConnectionClosed = null;

        protected MarketDataConnection() {
            _mktDataListener = new MarketDataSubscription();             
        }

        protected MarketDataConnection(IAbstractStreamingClient iclient)
        {
            _apiStreamingClient = iclient;
            _mktDataListener = new MarketDataSubscription();
        }
        
        static public MarketDataConnection Instance
        {
            get
            {
                if (_instance != null)
                    return _instance;
                if (Config.ReplayEnabled)
                    _instance = new ReplayConnection();
                else if (Config.MarketSelectorEnabled)
                    _instance = new MarketSelectorConnection();
                else if (Config.TradingEnabled)
                    _instance = new IGConnection();
                return _instance;
            }
            set
            {
                _instance = value;
            }
        }

        public abstract void Connect(TimerCallback connectionClosed);
        
        public IAbstractStreamingClient StreamClient
        {
            get { return _apiStreamingClient; }
        }

        public void SubscribeMarketData(MarketData mktData)
        {
            if (_mktDataListener.MarketData.Select(mktdata => mktdata.Id).Contains(mktData.Id))
                UnsubscribeMarketData(_mktDataListener.MarketData.Where(mktdata => mktdata.Id == mktData.Id).First());
            _mktDataListener.MarketData.Add(mktData);
            Log.Instance.WriteEntry("Subscribed " + mktData.Name + " to " + mktData.Id);
        }

        public void UnsubscribeMarketData(MarketData mktData)
        {
            var lstRemove = _mktDataListener.MarketData.Where(mktdata => mktdata.Id == mktData.Id).ToList();
            foreach (var selmktdata in lstRemove)
            {
                _mktDataListener.MarketData.Remove(selmktdata);
                Log.Instance.WriteEntry("Unsubscribed " + selmktdata.Name + " from " + selmktdata.Id);
            }
        }

        public virtual void StartListening()
        {
            _mktDataListener.StartListening();
        }

        public virtual void StopListening()
        {
            _mktDataListener.StopListening();
        }

        public void PublishMarketLevels(List<MarketData> mktData)
        {
            foreach (var mkt in mktData)
            {
                _apiStreamingClient.GetMarketDetails(mkt);
                PublisherConnection.Instance.Insert(mkt.Levels.Value);
            }
        }
    }

    public class MarketDataSubscription : HandyTableListenerAdapter
    {
        public List<MarketData> MarketData = new List<MarketData>();
        public SubscribedTableKey MarketDataTableKey = null;
        
        public void StartListening()
        {
            string[] epics = (from MarketData mktData in MarketData select mktData.Id).ToArray();
            string epicsMsg = string.Concat((from string epic in epics select epic + ", ").ToArray()).TrimEnd(new char[] { ',', ' ' });
            Log.Instance.WriteEntry("Subscribing to market data: " + epicsMsg + "...", System.Diagnostics.EventLogEntryType.Information);
            MarketDataConnection.Instance.StreamClient.Subscribe(epics, this);
        }

        public void StopListening()
        {
            string[] epics = (from MarketData mktData in MarketData select mktData.Id).ToArray();
            string epicsMsg = string.Concat((from string epic in epics select epic + ", ").ToArray());
            Log.Instance.WriteEntry("Unsubscribing to market data: " + epicsMsg + "...", System.Diagnostics.EventLogEntryType.Information);
            MarketDataConnection.Instance.StreamClient.Unsubscribe();            
        }

        public override void OnUpdate(int itemPos, string itemName, IUpdateInfo update)
        {
            try
            {
                L1LsPriceData priceData = L1LsPriceUpdateData(itemPos, itemName, update);
                //if (priceData.MarketState == "TRADEABLE" || priceData.MarketState == "REPLAY")
                //{
                foreach (var data in (from MarketData mktData in MarketData where itemName.Contains(mktData.Id) select mktData).ToList())
                {
                    var curTime = Config.ParseDateTimeUTC(priceData.UpdateTime);
                    if (Config.PublishingOpen(curTime))
                        data.FireTick(curTime, priceData); // Timestamps from IG are GMT (i.e. equivalent to UTC)
                }
            }
            catch (SEHException exc)
            {
                Log.Instance.WriteEntry("Market data update interop error: " + exc.ToString() + ", Error code: " + exc.ErrorCode, EventLogEntryType.Error);
            }
            catch (Exception ex)
            {
                Log.Instance.WriteEntry("Market data update error: " + ex.Message, System.Diagnostics.EventLogEntryType.Error);
                throw;
            }
        }
    }
}
