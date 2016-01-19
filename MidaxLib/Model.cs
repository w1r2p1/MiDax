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

        protected bool OnBuy(Signal signal, DateTime time, Price value, bool check)
        {
            if (check)
                return true;
            if (_tradingSignal != null)
            {
                if (signal.Id == _tradingSignal)
                    Buy(signal, time, value);
            }
            return true;
        }

        protected bool OnSell(Signal signal, DateTime time, Price value, bool check)
        {
            if (_tradingSignal != null)
            {
                if (signal.Id == _tradingSignal)
                {
                    if (!_ptf.GetPosition(signal.MarketData.Id).Closed)
                    {
                        if (_ptf.GetPosition(signal.MarketData.Id).Value >= 0)
                        {
                            Log.Instance.WriteEntry(time + " Signal " + signal.Id + ": Some trades are still open. last trade: " + signal.Trade.Id + " " + value.Bid + ". Closing all positions...", EventLogEntryType.Error);
                            CloseAllPositions(time, signal.MarketData.Id);
                        }
                        return false;
                    }
                    signal.Trade = new Trade(time, signal.MarketData.Id, SIGNAL_CODE.SELL, _amount, value.Bid);
                    if (!check)
                        Sell(signal, time, value);
                }
            }            
            return true;
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
            // get the level indicators for the day (low, high, close)
            foreach (MarketData mktData in _mktData)
                mktData.GetMarketLevels();
            foreach (MarketData mktData in _mktIndices)
                mktData.GetMarketLevels();  
            // subscribe indicators and signals to market data feed
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
            MarketDataConnection.Instance.PublishMarketLevels(_mktData);
            MarketDataConnection.Instance.PublishMarketLevels(_mktIndices);
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

        public void CloseAllPositions(DateTime time, string stockid = "")
        {
            foreach (var position in _ptf.Positions)
            {
                if (position.Value.Value != 0)
                {
                    if (stockid == "" || stockid == position.Value.Trade.Epic)
                        _ptf.ClosePosition(position.Value.Trade, time);
                }
            }
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
        protected MarketData _vix = null;
        protected SignalMacD _macD_low = null;
        protected SignalMacD _macD_high = null;
        protected SignalMacDCascade _macDCas = null;
        protected SignalANN _ann = null;
        protected List<decimal> _annWeights = null;

        public ModelMacD(MarketData daxIndex, List<MarketData> daxStocks, MarketData vix, List<MarketData> otherIndices, int lowPeriod = 2, int midPeriod = 10, int highPeriod = 60)
        {
            List<MarketData> mktData = new List<MarketData>();
            mktData.Add(daxIndex);
            mktData.AddRange(daxStocks);
            this._closingTime = Config.ParseDateTimeLocal(Config.Settings["TRADING_CLOSING_TIME"]);            
            this._mktData = mktData;
            this._daxIndex = daxIndex;
            this._daxStocks = daxStocks;
            this._vix = vix;
            if (this._vix != null)
                this._mktIndices.Add(this._vix);
            this._mktIndices.AddRange(otherIndices);
            this._macD_low = new SignalMacD(_daxIndex, lowPeriod, midPeriod);
            this._macD_high = new SignalMacD(_daxIndex, midPeriod, highPeriod, this._macD_low.IndicatorHigh);
            this._macDCas = new SignalMacDCascade(_daxIndex, midPeriod, highPeriod, this._macD_high.IndicatorLow, this._macD_high.IndicatorHigh);
            this._mktSignals.Add(this._macD_low);
            this._mktSignals.Add(this._macD_high);
            this._mktSignals.Add(this._macDCas);
            this._mktEODIndicators.Add(new IndicatorLevelMean(_daxIndex));
            this._mktEODIndicators.Add(new IndicatorLevelPivot(_daxIndex));
            this._mktEODIndicators.Add(new IndicatorLevelR1(_daxIndex));
            this._mktEODIndicators.Add(new IndicatorLevelR2(_daxIndex));
            this._mktEODIndicators.Add(new IndicatorLevelR3(_daxIndex));
            this._mktEODIndicators.Add(new IndicatorLevelS1(_daxIndex));
            this._mktEODIndicators.Add(new IndicatorLevelS2(_daxIndex));
            this._mktEODIndicators.Add(new IndicatorLevelS3(_daxIndex));
        }

        protected override void Buy(Signal signal, DateTime time, Price value)
        {
            if (_ptf.GetPosition(_daxIndex.Id).Value < 0)
            {
                signal.Trade.Price = value.Offer;
                _ptf.ClosePosition(signal.Trade, time);
                string tradeRef = signal.Trade == null ? "" : " " + signal.Trade.Reference;
                Log.Instance.WriteEntry(time + tradeRef + " Signal " + signal.Id + ": BUY " + signal.MarketData.Id + " " + value.Offer, EventLogEntryType.Information);
            }
        }

        protected override void Sell(Signal signal, DateTime time, Price value)
        {
            if (_ptf.GetPosition(_daxIndex.Id).Value > 0)
            {
                _ptf.BookTrade(signal.Trade);
                Log.Instance.WriteEntry(time + " Signal " + signal.Id + ": Unexpected positive position. SELL " + signal.Trade.Id + " " + value.Offer, EventLogEntryType.Error);
            }
            else if (_ptf.GetPosition(_daxIndex.Id).Value == 0)
            {
                if (time <= _closingTime)
                {
                    _ptf.BookTrade(signal.Trade);
                    string tradeRef = signal.Trade == null ? "" : " " + signal.Trade.Reference;
                    Log.Instance.WriteEntry(time + tradeRef + " Signal " + signal.Id + ": SELL " + signal.MarketData.Id + " " + value.Bid, EventLogEntryType.Information);
                }
            }
        }        
    }
}
