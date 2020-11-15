using NBitcoin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WalletWasabi.Blockchain.Analysis;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Blockchain.TransactionOutputs;
using WalletWasabi.Blockchain.Transactions;
using WalletWasabi.Models;

namespace WalletWasabi.Tests.Helpers
{
	public static class BitcoinMock
	{
		public static SmartTransaction RandomSmartTransaction(int othersInputCount = 1, int othersOutputCount = 1, int ownInputCount = 0, int ownOutputCount = 0)
			=> RandomSmartTransaction(othersInputCount, Enumerable.Repeat(Money.Coins(1m), othersOutputCount), Enumerable.Repeat((Money.Coins(1.1m), 1), ownInputCount), Enumerable.Repeat((Money.Coins(1m), 1), ownOutputCount));

		public static SmartTransaction RandomSmartTransaction(int othersInputCount, IEnumerable<Money> othersOutputs, IEnumerable<(Money value, int anonset)> ownInputs, IEnumerable<(Money value, int anonset)> ownOutputs)
		{
			var km = RandomKeyManager();
			return RandomSmartTransaction(othersInputCount, othersOutputs, ownInputs.Select(x => (x.value, x.anonset, RandomHdPubKey(km))), ownOutputs.Select(x => (x.value, x.anonset, RandomHdPubKey(km))));
		}

		public static SmartTransaction RandomSmartTransaction(int othersInputCount, IEnumerable<Money> othersOutputs, IEnumerable<(Money value, int anonset, HdPubKey hdpk)> ownInputs, IEnumerable<(Money value, int anonset, HdPubKey hdpk)> ownOutputs)
		{
			var tx = Transaction.Create(Network.Main);
			var walletInputs = new HashSet<SmartCoin>();
			var walletOutputs = new HashSet<SmartCoin>();

			for (int i = 0; i < othersInputCount; i++)
			{
				tx.Inputs.Add(RandomOutPoint());
			}
			var idx = (uint)othersInputCount - 1;
			foreach (var txo in ownInputs)
			{
				idx++;
				var sc = RandomSmartCoin(txo.hdpk, txo.value, idx, anonymitySet: txo.anonset);
				tx.Inputs.Add(sc.OutPoint);
				walletInputs.Add(sc);
			}

			foreach (var val in othersOutputs)
			{
				tx.Outputs.Add(new TxOut(val, new Key()));
			}
			idx = (uint)othersOutputs.Count() - 1;
			foreach (var txo in ownOutputs)
			{
				idx++;
				var hdpk = txo.hdpk;
				tx.Outputs.Add(new TxOut(txo.value, hdpk.P2wpkhScript));
				var sc = new SmartCoin(new SmartTransaction(tx, Height.Mempool), idx, hdpk);
				walletOutputs.Add(sc);
			}

			var stx = new SmartTransaction(tx, Height.Mempool);

			foreach (var sc in walletInputs)
			{
				stx.WalletInputs.Add(sc);
			}

			foreach (var sc in walletOutputs)
			{
				stx.WalletOutputs.Add(sc);
			}

			return stx;
		}

		public static HdPubKey RandomHdPubKey(KeyManager km)
			=> km.GenerateNewKey(SmartLabel.Empty, KeyState.Clean, isInternal: false);

		public static SmartCoin RandomSmartCoin(HdPubKey pubKey, decimal amountBtc, uint index = 0, bool confirmed = true, int anonymitySet = 1)
			=> RandomSmartCoin(pubKey, Money.Coins(amountBtc), index, confirmed, anonymitySet);

		public static SmartCoin RandomSmartCoin(HdPubKey pubKey, Money amount, uint index = 0, bool confirmed = true, int anonymitySet = 1)
		{
			var height = confirmed ? new Height(CryptoHelpers.RandomInt(0, 200)) : Height.Mempool;
			pubKey.SetKeyState(KeyState.Used);
			var tx = Transaction.Create(Network.Main);
			tx.Outputs.Add(new TxOut(amount, pubKey.P2wpkhScript));
			var stx = new SmartTransaction(tx, height);
			pubKey.AnonymitySet = anonymitySet;
			return new SmartCoin(stx, index, pubKey);
		}

		public static OutPoint RandomOutPoint()
			=> new OutPoint(RandomUint256(), (uint)CryptoHelpers.RandomInt(0, 100));

		public static uint256 RandomUint256()
		{
			var rand = new UnsecureRandom();
			var bytes = new byte[32];
			rand.GetBytes(bytes);
			return new uint256(bytes);
		}

		public static TransactionFactory CreateTransactionFactory(
		   IEnumerable<(string Label, int KeyIndex, decimal Amount, bool Confirmed, int AnonymitySet)> coins,
		   bool allowUnconfirmed = true,
		   bool watchOnly = false)
		{
			var password = "foo";
			var keyManager = watchOnly ? RandomWatchOnlyKeyManager() : RandomKeyManager(password);

			keyManager.AssertCleanKeysIndexed();

			var coinArray = coins.ToArray();
			var keys = keyManager.GetKeys().Take(coinArray.Length).ToArray();
			for (int i = 0; i < coinArray.Length; i++)
			{
				var c = coinArray[i];
				var k = keys[c.KeyIndex];
				k.SetLabel(c.Label);
			}

			var scoins = coins.Select(x => RandomSmartCoin(keys[x.KeyIndex], x.Amount, 0, x.Confirmed, x.AnonymitySet)).ToArray();
			foreach (var coin in scoins)
			{
				foreach (var sameLabelCoin in scoins.Where(c => !c.HdPubKey.Label.IsEmpty && c.HdPubKey.Label == coin.HdPubKey.Label))
				{
					sameLabelCoin.HdPubKey.Cluster = coin.HdPubKey.Cluster;
				}
			}
			var coinsView = new CoinsView(scoins);
			var transactionStore = new AllTransactionStoreMock(workFolderPath: ".", Network.Main);
			return new TransactionFactory(Network.Main, keyManager, coinsView, transactionStore, password, allowUnconfirmed);
		}

		public static KeyManager RandomKeyManager(string password = "blahblahblah")
			=> KeyManager.CreateNew(out var _, password);

		public static KeyManager RandomWatchOnlyKeyManager()
			=> KeyManager.CreateNewWatchOnly(new Mnemonic(Wordlist.English, WordCount.Twelve).DeriveExtKey().Neuter());

		public static BlockchainAnalyzer RandomBlockchainAnalyzer(int privacyLevelThreshold = 100)
			=> new BlockchainAnalyzer(privacyLevelThreshold);
	}
}
