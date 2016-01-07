﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IGPublicPcl;

namespace MidaxLib
{
    public abstract class Model
    {
        protected string _tradingSignal = null;
        protected int _amount = 0;
        protected Portfolio _ptf = null;
        protected List<MarketData> _mktData = new List<MarketData>();
        protected List<Signal> _mktSignals = new List<Signal>();
        protected List<MarketData> _mktIndices = new List<MarketData>();
        protected List<Indicator> _mktIndicators = new List<Indicator>();
        protected List<ILevelPublisher> _mktEODIndicators = new List<ILevelPublisher>();
        protected bool _replayPopup = false;

        public Portfolio PTF { get { return _ptf; } }
        
        public Model()
        {
            if (Config.Settings.ContainsKey("TRADING_SIGNAL"))
                _tradingSignal = Config.Settings["TRADING_SIGNAL"];
            if (Config.Settings.ContainsKey("REPLAY_POPUP"))
                _replayPopup = Config.Settings["REPLAY_POPUP"] == "1";
            _amount = Config.MarketSelectorEnabled ? 0 : int.Parse(Config.Settings["TRADING_LIMIT_PER_BP"]);
            _ptf = new Portfolio(MarketDataConnection.Instance.StreamClient);
        }

        protected void OnBuy(Signal signal, DateTime time, Price value)
        {
            if (_tradingSignal != null)
                if (signal.Id == _tradingSignal)
                    signal.Trade = new Trade(time, signal.Asset.Id, SIGNAL_CODE.BUY, _amount, value.Offer);
            Buy(signal, time, value);
        }

        protected void OnSell(Signal signal, DateTime time, Price value)
        {
            if (_tradingSignal != null)
                if (signal.Id == _tradingSignal)
                    signal.Trade = new Trade(time, signal.Asset.Id, SIGNAL_CODE.SELL, _amount, value.Bid);
            Sell(signal, time, value);
        }

        protected abstract void Buy(Signal signal, DateTime time, Price value);
        protected abstract void Sell(Signal signal, DateTime time, Price value);

        protected virtual void OnUpdateMktData(MarketData mktData, DateTime updateTime, Price value)
        {
        }
        protected virtual void OnUpdateIndicator(MarketData mktData, DateTime updateTime, Price value)
        {
        }
        
        public void StartSignals()
        {
            foreach (MarketData idx in _mktIndices)
                idx.Subscribe(OnUpdateMktData);
            foreach (Indicator ind in _mktIndicators)
                ind.Subscribe(OnUpdateIndicator);
            foreach (Signal sig in _mktSignals)
                sig.Subscribe(OnBuy, OnSell);  
            MarketDataConnection.Instance.StartListening();
        }

        public string StopSignals()
        {
            MarketDataConnection.Instance.StopListening();
            Log.Instance.WriteEntry("Publishing indicator levels...", EventLogEntryType.Information);
            foreach (var indicator in _mktEODIndicators)
                indicator.Publish(Config.ParseDateTimeLocal(Config.Settings["PUBLISHING_STOP_TIME"]));
            string status = PublisherConnection.Instance.Close();
            foreach (Signal sig in _mktSignals)
            {
                sig.Unsubscribe();
                sig.Clear();
            }
            foreach (Indicator ind in _mktIndicators)
            {
                ind.Unsubscribe(OnUpdateIndicator);
                ind.Clear();
            }
            foreach (MarketData idx in _mktIndices)
            {
                idx.Unsubscribe(OnUpdateMktData);
                idx.Clear();
            }
            foreach (MarketData stock in _mktData)
                stock.Clear();
            return status;
        }

        public void CloseAllPositions()
        {
            foreach (var position in _ptf.Positions)
            {
                if (position.Value.Value != 0)
                    _ptf.ClosePosition(position.Value.Trade);
            }
        }

        public void PublishMarketLevels()
        {
            MarketDataConnection.Instance.PublishMarketLevels(_mktData);
        }

        public void BookTrade(Trade trade)
        {
            _ptf.BookTrade(trade);
        }

        public virtual void ProcessError(string message, string expected = "")
        {            
        }
    }

    public class ModelMacD : Model
    {
        protected DateTime _closingTime = DateTime.MinValue;
        protected MarketData _daxIndex = null;
        protected List<MarketData> _daxStocks = null;
        protected List<MarketData> _volatilityIndices = null;
        protected SignalMacD _macD_low = null;
        protected SignalMacD _macD_high = null;

        public ModelMacD(MarketData daxIndex, List<MarketData> daxStocks, List<MarketData> volatilityIndices, int lowPeriod = 2, int midPeriod = 10, int highPeriod = 60)
        {
            List<MarketData> mktData = new List<MarketData>();
            mktData.Add(daxIndex);
            mktData.AddRange(daxStocks);
            this._closingTime = Config.ParseDateTimeLocal(Config.Settings["TRADING_CLOSING_TIME"]);            
            this._mktData = mktData;
            this._daxIndex = daxIndex;
            this._daxStocks = daxStocks;
            this._volatilityIndices = volatilityIndices;
            this._mktIndices.AddRange(volatilityIndices);
            this._macD_low = new SignalMacD(_daxIndex, lowPeriod, midPeriod);
            this._macD_high = new SignalMacD(_daxIndex, midPeriod, highPeriod, this._macD_low.IndicatorHigh);
            this._mktSignals.Add(this._macD_low);
            this._mktSignals.Add(this._macD_high);
            this._mktIndicators.Add(new IndicatorLinearRegression(_daxIndex, new TimeSpan(0, 0, lowPeriod * 30)));
            this._mktIndicators.Add(new IndicatorLinearRegression(_daxIndex, new TimeSpan(0, 0, midPeriod * 30)));
            this._mktIndicators.Add(new IndicatorLinearRegression(_daxIndex, new TimeSpan(0, 0, highPeriod * 30)));
            this._mktEODIndicators.Add(new IndicatorLevelMean(_daxIndex));
        }

        protected override void Buy(Signal signal, DateTime time, Price value)
        {
            if (_ptf.GetPosition(_daxIndex.Id).Value < 0)
            {
                _ptf.ClosePosition(signal.Trade);
                string tradeRef = signal.Trade == null ? "" : " " + signal.Trade.Reference;
                Log.Instance.WriteEntry(time + tradeRef + " Signal " + signal.Id + ": BUY " + signal.Asset.Id + " " + value.Offer, EventLogEntryType.Information);
            }
        }
         
        protected override void Sell(Signal signal, DateTime time, Price value)
        {
            if (_ptf.GetPosition(_daxIndex.Id).Value >= 0)
            {
                if (_ptf.GetPosition(_daxIndex.Id).Value > 0)
                {
                    Log.Instance.WriteEntry(time + " Signal " + signal.Id + ": Unexpected positive position. SELL " + signal.Trade.Id + " " + value.Offer, EventLogEntryType.Error);
                    _ptf.ClosePosition(signal.Trade);
                }
                else if (time <= _closingTime)
                {
                    if (_ptf.GetPosition(_daxIndex.Id).NbPositionsOpen != 0)
                    {
                        Log.Instance.WriteEntry(time + " Signal " + signal.Id + ": Some trades are still open. SELL " + signal.Trade.Id + " " + value.Bid, EventLogEntryType.Error);
                        _ptf.ClosePosition(signal.Trade);
                    }
                    else
                    {
                        _ptf.BookTrade(signal.Trade);
                        string tradeRef = signal.Trade == null ? "" : " " + signal.Trade.Reference;
                        Log.Instance.WriteEntry(time + tradeRef + " Signal " + signal.Id + ": SELL " + signal.Asset.Id + " " + value.Bid, EventLogEntryType.Information);
                    }
                }
            }
        }
    }
}
