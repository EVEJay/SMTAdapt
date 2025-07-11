﻿//-----------------------------------------------------------------------
// ZKillboard ReDisQ feed
//-----------------------------------------------------------------------
using System.ComponentModel;
using Timer = System.Timers.Timer;

namespace SMT.EVEData
{
    /// <summary>
    /// The ZKillboard RedisQ representation
    /// </summary>
    public class ZKillRedisQ
    {
        public string VerString = "ABC123";
        private BackgroundWorker backgroundWorker;

        private string QueueID;

        /// <summary>
        /// Gets or sets the Stream of the last few kills from ZKillBoard
        /// </summary>
        public List<ZKBDataSimple> KillStream { get; set; }

        /// <summary>
        /// Kills Added Event Handler
        /// </summary>
        public delegate void KillsAddedHandler();

        /// <summary>
        /// Kills Added Events
        /// </summary>
        public event KillsAddedHandler KillsAddedEvent;

        public int KillExpireTimeMinutes { get; set; }

        /// <summary>
        ///
        /// </summary>
        public bool PauseUpdate { get; set; }

        /// <summary>
        /// Initialise the ZKB feed system
        /// </summary>
        public void Initialise()
        {
            KillStream = new List<ZKBDataSimple>();

            // set the queue id which is now required
            QueueID = "SMT_" + EVEDataUtils.Misc.RandomString(10);

            backgroundWorker = new BackgroundWorker();
            backgroundWorker.WorkerSupportsCancellation = true;
            backgroundWorker.WorkerReportsProgress = false;
            backgroundWorker.DoWork += zkb_DoWork;
            backgroundWorker.RunWorkerCompleted += zkb_DoWorkComplete;

            Timer dp = new Timer(1000);
            dp.Elapsed += Dp_Tick;
            dp.AutoReset = true;
            dp.Enabled = true;
        }

        public void ShutDown()
        {
            backgroundWorker.CancelAsync();
        }

        private void Dp_Tick(object sender, EventArgs e)
        {
            if(!backgroundWorker.IsBusy && !PauseUpdate)
            {
                backgroundWorker.RunWorkerAsync();
            }
            else
            {
            }
        }

        private void zkb_DoWork(object sender, DoWorkEventArgs e)
        {
            string redistURL = $"https://zkillredisq.stream/listen.php?queueID=SMT_{QueueID}";
            string strContent = string.Empty;
            try
            {
                HttpClient hc = new HttpClient();
                var response = hc.GetAsync(redistURL).Result;
                if(response.IsSuccessStatusCode)
                {
                    strContent = response.Content.ReadAsStringAsync().Result;
                }
                else
                {
                    strContent = "Error";
                }
            }
            catch
            {
                e.Result = -1;
                return;
            }

            // todo : investigate issues beyond a ban.. the 429 should be handled with the null
            if(strContent == "Error")
            {
                Thread.Sleep(500000);

                e.Result = 0;
                return;
            }

            ZKBData.ZkbData z = ZKBData.ZkbData.FromJson(strContent);
            if(z != null && z.Package != null)
            {
                ZKBDataSimple zs = new ZKBDataSimple();
                zs.KillID = long.Parse(z.Package.KillId.ToString());
                zs.VictimAllianceID = int.Parse(z.Package.Killmail.Victim.AllianceId.ToString());
                zs.VictimCharacterID = int.Parse(z.Package.Killmail.Victim.CharacterId.ToString());
                zs.VictimCorpID = int.Parse(z.Package.Killmail.Victim.CharacterId.ToString());
                zs.SystemName = EveManager.Instance.GetEveSystemNameFromID(z.Package.Killmail.SolarSystemId);
                if(zs.SystemName == string.Empty)
                {
                    zs.SystemName = z.Package.Killmail.SolarSystemId.ToString();
                }

                zs.KillTime = z.Package.Killmail.KillmailTime.ToLocalTime();
                string shipID = z.Package.Killmail.Victim.ShipTypeId.ToString();
                if(EveManager.Instance.ShipTypes.ContainsKey(shipID))
                {
                    zs.ShipType = EveManager.Instance.ShipTypes[shipID];
                }
                else
                {
                    zs.ShipType = "Unknown (" + shipID + ")";
                }

                zs.VictimAllianceName = EveManager.Instance.GetAllianceName(zs.VictimAllianceID);

                KillStream.Insert(0, zs);

                if(KillsAddedEvent != null)
                {
                    KillsAddedEvent();
                }
            }
            else
            {
                // no killmail, wait 10s
                Thread.Sleep(10000);
            }

            e.Result = 0;
        }

        private void zkb_DoWorkComplete(object sender, RunWorkerCompletedEventArgs e)
        {
            bool updatedKillList = false;

            List<int> AllianceIDs = new List<int>();

            for(int i = KillStream.Count - 1; i >= 0; i--)
            {
                if(KillStream[i].VictimAllianceName == string.Empty)
                {
                    if(!EveManager.Instance.AllianceIDToTicker.ContainsKey(KillStream[i].VictimAllianceID) && !AllianceIDs.Contains(KillStream[i].VictimAllianceID) && KillStream[i].VictimAllianceID != 0)
                    {
                        AllianceIDs.Add(KillStream[i].VictimAllianceID);
                    }
                    else
                    {
                        KillStream[i].VictimAllianceName = EveManager.Instance.GetAllianceName(KillStream[i].VictimAllianceID);
                    }
                }

                if(KillStream[i].KillTime + TimeSpan.FromMinutes(KillExpireTimeMinutes) < DateTimeOffset.Now)
                {
                    KillStream.RemoveAt(i);

                    updatedKillList = true;
                }
            }
            if(AllianceIDs.Count > 0)
            {
                EveManager.Instance.ResolveAllianceIDs(AllianceIDs);
            }

            if(updatedKillList)
            {
                // kills are coming in so fast that this is redundant
                if(KillsAddedEvent != null)
                {
                    KillsAddedEvent();
                }
            }
        }

        /// <summary>
        /// A simple class with the Kill Highlights
        /// </summary>
        public class ZKBDataSimple : INotifyPropertyChanged
        {
            private string m_victimAllianceName;

            public event PropertyChangedEventHandler PropertyChanged;

            /// <summary>
            /// Gets or sets the ZKillboard Kill ID
            /// </summary>
            public long KillID { get; set; }

            /// <summary>
            /// Gets or sets the time of the kill
            /// </summary>
            public DateTimeOffset KillTime { get; set; }

            /// <summary>
            /// Gets or sets the Ship Lost in this kill
            /// </summary>
            public string ShipType { get; set; }

            /// <summary>
            /// Gets or sets the System ID the kill was in
            /// </summary>
            public string SystemName { get; set; }

            /// <summary>
            /// Gets or sets the Victims Alliance ID
            /// </summary>
            public int VictimAllianceID { get; set; }

            /// <summary>
            /// Gets or sets the Victims Alliance Name
            /// </summary>
            public string VictimAllianceName
            {
                get
                {
                    return m_victimAllianceName;
                }
                set
                {
                    m_victimAllianceName = value;
                    OnPropertyChanged("VictimAllianceName");
                }
            }

            /// <summary>
            /// Gets or sets the character ID of the victim
            /// </summary>
            public int VictimCharacterID { get; set; }

            /// <summary>
            /// Gets or sets the Victim's corp ID
            /// </summary>
            public int VictimCorpID { get; set; }

            public override string ToString()
            {
                string allianceTicker = EVEData.EveManager.Instance.GetAllianceTicker(VictimAllianceID);
                if(allianceTicker == string.Empty)
                {
                    allianceTicker = VictimAllianceID.ToString();
                }

                return string.Format("System: {0}, Alliance: {1}, Ship {2}", SystemName, allianceTicker, ShipType);
            }

            protected void OnPropertyChanged(string name)
            {
                PropertyChangedEventHandler handler = PropertyChanged;
                if(handler != null)
                {
                    handler(this, new PropertyChangedEventArgs(name));
                }
            }
        }
    }
}