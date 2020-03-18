﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Neo.Common.Storage.SQLiteModules;
using Neo.IO;
using Neo.Models;

namespace Neo.Common.Storage
{
    public class TrackDB : IDisposable
    {

        private SQLiteContext _db;
        private uint _magic;
        private DateTime _createTime = DateTime.Now;

        public TimeSpan LiveTime => DateTime.Now - _createTime;
        public TrackDB()
        {
            _magic = ProtocolSettings.Default.Magic;
            if (!Directory.Exists("Data_Track"))
            {
                Directory.CreateDirectory("Data_Track");
            }
            _db = new SQLiteContext(Path.Combine($"Data_Track", $"track.{_magic}.db"));
        }

        public void Commit()
        {
            _db.SaveChanges();
        }

        #region SyncIndex

        public void AddSyncIndex(uint index)
        {
            _db.SyncIndexes.Add(new SyncIndex() { BlockHeight = index });
        }

        public bool GetSyncIndex(uint index)
        {
            return _db.SyncIndexes.FirstOrDefault(s => s.BlockHeight == index) != null;
        }

        #endregion

        #region ExeResult

        public void AddExecuteResult(ExecuteResultEntity execResult)
        {
            _db.ExecuteResults.Add(execResult);
            //_db.SaveChanges();
        }


        public IEnumerable<NotifyEventEntity> GetNotifyEventsByTxId(UInt256 txId)
        {
            return _db.ExecuteResults.Include(e => e.Notifications).Where(e => e.TxId == txId.ToBigEndianHex()).SelectMany(e => e.Notifications);
        }

        #endregion

        #region transfer

        public AddressEntity GetOrCreateAddress(UInt160 address)
        {
            if (address == null) return null;
            var addr = address.ToBigEndianHex();
            var old = _db.Addresses.FirstOrDefault(a => a.Address == addr);
            if (old == null)
            {
                old = new AddressEntity() { Hash = address.ToArray(), Address = addr };
                _db.Addresses.Add(old);
                _db.SaveChanges();
            }
            return old;
        }

        public AssetEntity GetOrCreateAsset(AssetInfo asset)
        {
            var assetScriptHash = asset.Asset.ToBigEndianHex();
            var old = _db.Assets.FirstOrDefault(a => a.Asset == assetScriptHash);
            if (old == null)
            {
                old = new AssetEntity() { Hash = asset.Asset.ToArray(), Asset = assetScriptHash, Name = asset.Name, Symbol = asset.Symbol, Decimals = asset.Decimals,TotalSupply = asset.TotalSupply.ToByteArray()};
                _db.Assets.Add(old);
                _db.SaveChanges();
            }
            return old;
        }

        public void AddTransfer(TransferInfo newTransaction)
        {
            var from = GetOrCreateAddress(newTransaction.From);
            var to = GetOrCreateAddress(newTransaction.To);
            var asset = GetOrCreateAsset(newTransaction.AssetInfo);

            var tran = new Nep5TransactionEntity
            {
                BlockHeight = newTransaction.BlockHeight,
                TxId = newTransaction.TxId.ToBigEndianHex(),
                FromId = from?.Id,
                ToId = to.Id,
                FromBalance = newTransaction.FromBalance.ToByteArray(),
                ToBalance = newTransaction.ToBalance.ToByteArray(),
                Amount = newTransaction.Amount.ToByteArray(),
                AssetId = asset.Id,
                Time = newTransaction.TimeStamp.FromTimestampMS(),
            };
            _db.Nep5Transactions.Add(tran);
            UpdateBalance(from, asset, newTransaction.FromBalance, newTransaction.BlockHeight);
            UpdateBalance(to, asset, newTransaction.ToBalance, newTransaction.BlockHeight);
        }

        public void UpdateBalance(AddressEntity address, AssetEntity asset, BigInteger balance, uint height)
        {
            if (address == null) return;
            var old = _db.AssetBalances.FirstOrDefault(a => a.AddressId == address.Id && a.AssetId == asset.Id);
            if (old == null)
            {
                _db.AssetBalances.Add(new AssetBalanceEntity()
                { AddressId = address.Id, AssetId = asset.Id, Balance = balance.ToByteArray(), BlockHeight = height });
            }
            else
            {
                old.Balance = balance.ToByteArray();
                old.BlockHeight = height;
            }
        }




        /// <summary>
        ///  Paged by Transactions
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public PageList<TransferInfo> FindTransactions(TrackFilter filter)
        {
            var query = BuildQuery(filter);
            var pageList = new PageList<TransferInfo>();
            var pageIndex = filter.PageIndex <= 0 ? 0 : filter.PageIndex - 1;
            pageList.TotalCount = query.GroupBy(q => q.TxId).Count();
            pageList.PageIndex = pageIndex + 1;
            pageList.PageSize = filter.PageSize;
            if (filter.PageSize > 0)
            {
                var txIds = query.GroupBy(q => new { q.TxId, q.Time }).OrderByDescending(g => g.Key.Time).Select(g => g.Key)
                    .Skip(pageIndex * filter.PageSize)
                    .Take(filter.PageSize).Select(g => g.TxId).ToList();
                pageList.List.AddRange(query.Where(q => txIds.Contains(q.TxId)).OrderByDescending(r => r.Time).ToList().Select(ToNep5TransferInfo));
            }
            return pageList;
        }


        /// <summary>
        /// Paged by transfer
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public PageList<TransferInfo> FindTransfer(TrackFilter filter)
        {
            var query = BuildQuery(filter);
            var pageList = new PageList<TransferInfo>();
            var pageIndex = filter.PageIndex <= 0 ? 0 : filter.PageIndex - 1;
            pageList.TotalCount = query.Count();
            pageList.PageIndex = pageIndex + 1;
            pageList.PageSize = filter.PageSize;
            if (filter.PageSize > 0)
            {
                pageList.List.AddRange(query.OrderByDescending(r => r.Time).Skip(pageIndex * filter.PageSize)
                    .Take(filter.PageSize).ToList().Select(ToNep5TransferInfo));
            }
            return pageList;
        }


        /// <summary>
        /// query balances
        /// </summary>
        /// <param name="filter"></param>
        /// <returns></returns>
        public IEnumerable<BalanceInfo> FindAssetBalance(BalanceFilter filter)
        {
            IQueryable<AssetBalanceEntity> query = _db.AssetBalances.Include(a => a.Address).Include(a => a.Asset);
            if (filter.Addresses.NotEmpty())
            {
                var addrs = filter.Addresses.Select(a => a.ToBigEndianHex()).ToList();
                query = query.Where(q => addrs.Contains(q.Address.Address));
            }

            if (filter.Assets.NotEmpty())
            {
                var assets = filter.Assets.Select(a => a.ToBigEndianHex()).ToList();
                query = query.Where(q => assets.Contains(q.Asset.Asset));
            }

            var balances = query.ToList();
            return balances.Select(b => new BalanceInfo()
            {
                Address = UInt160.Parse(b.Address.Address),
                Asset = UInt160.Parse(b.Asset.Asset),
                AssetName = b.Asset.Name,
                AssetSymbol = b.Asset.Symbol,
                AssetDecimals = b.Asset.Decimals,
                Balance = new BigInteger(b.Balance),
                BlockHeight = b.BlockHeight,
            });
        }


        public IEnumerable<AssetEntity> GetAllAssets()
        {
            return _db.Assets.ToList();
        }

        #endregion




        #region Private


        private IQueryable<Nep5TransactionEntity> BuildQuery(TrackFilter filter)
        {
            IQueryable<Nep5TransactionEntity> query = _db.Nep5Transactions
                .Include(t => t.From)
                .Include(t => t.To)
                .Include(t => t.Asset);

            if (filter.FromOrTo.NotEmpty())
            {
                var addresses = filter.FromOrTo.Select(a => a.ToBigEndianHex()).ToList();
                query = query.Where(r => addresses.Contains(r.From.Address) || addresses.Contains(r.To.Address));
            }
            if (filter.From.NotEmpty())
            {
                var addresses = filter.From.Select(a => a.ToBigEndianHex()).ToList();
                query = query.Where(r => addresses.Contains(r.From.Address));
            }
            if (filter.To.NotEmpty())
            {
                var addresses = filter.To.Select(a => a.ToBigEndianHex()).ToList();
                query = query.Where(r => addresses.Contains(r.To.Address));
            }
            if (filter.StartTime != null)
            {
                query = query.Where(r => r.Time >= filter.StartTime.Value.ToUniversalTime());
            }
            if (filter.EndTime != null)
            {
                query = query.Where(r => r.Time <= filter.EndTime.Value.ToUniversalTime());
            }
            if (filter.Asset != null)
            {
                query = query.Where(r => r.Asset.Asset == filter.Asset.ToBigEndianHex());
            }
            if (filter.BlockHeight != null)
            {
                query = query.Where(r => r.BlockHeight == filter.BlockHeight);
            }
            if (filter.TxIds.NotEmpty())
            {
                var txids = filter.TxIds.Select(t => t.ToBigEndianHex()).Distinct().ToList();
                query = query.Where(r => txids.Contains(r.TxId));
            }

            return query;
        }


        private TransferInfo ToNep5TransferInfo(Nep5TransactionEntity entity)
        {
            return new TransferInfo()
            {
                BlockHeight = entity.BlockHeight,
                TxId = UInt256.Parse(entity.TxId),
                From = entity.From != null ? UInt160.Parse(entity.From.Address) : null,
                To = UInt160.Parse(entity.To.Address),
                FromBalance = new BigInteger(entity.FromBalance),
                ToBalance = new BigInteger(entity.ToBalance),
                Amount = new BigInteger(entity.Amount),
                Asset = UInt160.Parse(entity.Asset.Asset),

                TimeStamp = entity.Time.AsUtcTime().ToTimestampMS(),
            };
        }

        #endregion




        public void Dispose()
        {
            _db?.Dispose();
        }
    }
}
